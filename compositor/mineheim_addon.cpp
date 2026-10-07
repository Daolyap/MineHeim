// ReShade add-on for Valheim (Direct3D 11): uploads Minecraft's latest frame (shared memory "Local\MineHeimFrame")
// into textures that MineHeim.fx composites into Valheim's picture against Valheim's depth buffer.
//
// Adapted from the compositor of rehan-remade/universal-modder's examples/minecraft-gta5-passthrough (MIT). What is
// different here: it is a stand-alone add-on (MineHeim.addon64, loaded by ReShade itself), and Valheim's camera
// comes from the plugin through the shared memory "Local\MineHeimHostPose" instead of from a game script. A
// render-thread marker (VCRenderEvent, queued by the plugin into the main camera's command stream) says which frame is
// being drawn, so the pose used for re-projection is the one that frame was rendered with.
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <reshade.hpp>

using namespace reshade::api;

extern "C" __declspec(dllexport) const char *NAME = "MineHeim";
extern "C" __declspec(dllexport) const char *DESCRIPTION = "Draws real Minecraft into Valheim's frame, depth-tested against Valheim's depth buffer.";

namespace
{
	constexpr const wchar_t *kFrameName = L"Local\\MineHeimFrame";
	constexpr const wchar_t *kPoseName = L"Local\\MineHeimHostPose";
	constexpr uint32_t kFrameMagic = 0x5450434D; // "MCPT"
	constexpr uint32_t kPoseMagic = 0x50484356;  // "VCHP"
	constexpr int kHeader = 4096;
	constexpr int kSlotDesc = 256;
	constexpr int kSlotDescBytes = 128;
	constexpr int kPoseHeader = 64, kPoseSlots = 8, kPoseStride = 128;
	constexpr const char *kEffect = "MineHeim.fx";

	// set by the plugin's render-thread marker: the frame number of the camera being drawn right now
	std::atomic<int32_t> g_renderFrame{0};
	std::atomic<int32_t> g_markerCalls{0};
	// what the last composited frame used (for the screenshot sidecar)
	std::atomic<int64_t> g_usedPoseFrame{0}, g_newestPoseFrame{0}, g_usedMcFrame{0};
	std::atomic<bool> g_usedActive{false};
	// the pose pair and warp last uploaded to the effect (diagnostics for the screenshot sidecar; render thread only)
	double g_dbgHost[7] = {}, g_dbgMc[7] = {}, g_dbgWarp[12] = {};

	struct Pose
	{
		float yaw = 0, pitch = 0, roll = 0, fov = 65, nearClip = 0.1f, farClip = 1000.0f;
		double x = 0, y = 0, z = 0;
		int64_t frame = 0, qpc = 0;
		int32_t flags = 0;
		bool valid = false;
	};
	Pose g_mcPose;

	HANDLE g_frameMapping = nullptr, g_poseMapping = nullptr;
	const uint8_t *g_frameView = nullptr, *g_poseView = nullptr;
	int32_t g_slots = 0;
	int64_t g_stride = 0;
	int64_t g_lastPublish = -1;
	DWORD g_nextOpenAttempt = 0;
	LARGE_INTEGER g_qpcFreq;

	struct Layer
	{
		resource tex = {0};
		resource_view srv = {0};
	};
	Layer g_world, g_depth, g_overlay;
	uint32_t g_width = 0, g_height = 0;
	bool g_hasFrame = false;
	float g_mcNear = 0.05f, g_mcFar = 1024.0f;
	int32_t g_mcFlags = 7;
	int32_t g_lastPoseFrame = -1;
	// the plugin's pose counter, watched on this side's clock (the plugin's Stopwatch is not guaranteed to be QPC under Mono)
	int64_t g_lastPoseWritten = -1;
	DWORD g_poseChangedTick = 0;
	DWORD g_nextStatus = 0, g_nextUniformReport = 0;

	template <typename T>
	T read(const uint8_t *p)
	{
		T v;
		std::memcpy(&v, p, sizeof(T));
		return v;
	}

	bool open_mappings()
	{
		if (g_frameView != nullptr && g_poseView != nullptr)
			return true;
		if (GetTickCount() < g_nextOpenAttempt)
			return false;
		g_nextOpenAttempt = GetTickCount() + 1000;
		if (g_frameView == nullptr)
		{
			g_frameMapping = OpenFileMappingW(FILE_MAP_READ, FALSE, kFrameName);
			if (g_frameMapping != nullptr)
			{
				const auto *header = static_cast<const uint8_t *>(MapViewOfFile(g_frameMapping, FILE_MAP_READ, 0, 0, kHeader));
				if (header != nullptr && read<uint32_t>(header) == kFrameMagic)
				{
					g_slots = read<int32_t>(header + 12);
					g_stride = read<int64_t>(header + 16);
					UnmapViewOfFile(header);
					g_frameView = static_cast<const uint8_t *>(
						MapViewOfFile(g_frameMapping, FILE_MAP_READ, 0, 0, static_cast<SIZE_T>(kHeader + g_stride * g_slots)));
					if (g_frameView != nullptr)
						reshade::log::message(reshade::log::level::info, "MineHeim: connected to Minecraft's frame export");
				}
				else if (header != nullptr)
				{
					UnmapViewOfFile(header);
				}
				if (g_frameView == nullptr)
				{
					CloseHandle(g_frameMapping);
					g_frameMapping = nullptr;
				}
			}
		}
		if (g_poseView == nullptr)
		{
			g_poseMapping = OpenFileMappingW(FILE_MAP_READ, FALSE, kPoseName);
			if (g_poseMapping != nullptr)
			{
				g_poseView = static_cast<const uint8_t *>(
					MapViewOfFile(g_poseMapping, FILE_MAP_READ, 0, 0, kPoseHeader + kPoseSlots * kPoseStride));
				if (g_poseView != nullptr && read<uint32_t>(g_poseView) != kPoseMagic)
				{
					UnmapViewOfFile(g_poseView);
					g_poseView = nullptr;
				}
				if (g_poseView == nullptr)
				{
					CloseHandle(g_poseMapping);
					g_poseMapping = nullptr;
				}
				else
				{
					reshade::log::message(reshade::log::level::info, "MineHeim: connected to the plugin's host pose");
				}
			}
		}
		return g_frameView != nullptr && g_poseView != nullptr;
	}

	/// Valheim's pose for `wanted` (0: the newest), as the plugin wrote it. False if the slot was overwritten or none exists.
	bool read_pose(int64_t wanted, Pose &out)
	{
		if (g_poseView == nullptr)
			return false;
		const int64_t written = read<int64_t>(g_poseView + 16);
		bool found = false;
		int64_t best = -1;
		for (int i = 0; i < kPoseSlots; ++i)
		{
			const uint8_t *slot = g_poseView + kPoseHeader + kPoseStride * i;
			const int64_t seq = read<int64_t>(slot);
			if (seq == 0 || (seq & 1))
				continue;
			Pose p;
			p.frame = read<int64_t>(slot + 8);
			p.qpc = read<int64_t>(slot + 16);
			p.x = read<double>(slot + 24);
			p.y = read<double>(slot + 32);
			p.z = read<double>(slot + 40);
			p.yaw = read<float>(slot + 48);
			p.pitch = read<float>(slot + 52);
			p.roll = read<float>(slot + 56);
			p.fov = read<float>(slot + 60);
			p.nearClip = read<float>(slot + 64);
			p.farClip = read<float>(slot + 68);
			p.flags = read<int32_t>(slot + 72);
			if (read<int64_t>(slot) != seq)
				continue; // being rewritten
			p.valid = true;
			const bool match = wanted != 0 && (p.frame & 0x7fffffff) == (wanted & 0x7fffffff);
			if (match)
			{
				out = p;
				return true;
			}
			if (wanted == 0 && p.frame > best)
			{
				best = p.frame;
				out = p;
				found = true;
			}
		}
		(void)written;
		return found;
	}

	void destroy_layers(device *dev)
	{
		for (Layer *layer : {&g_world, &g_depth, &g_overlay})
		{
			if (layer->srv.handle != 0)
				dev->destroy_resource_view(layer->srv);
			if (layer->tex.handle != 0)
				dev->destroy_resource(layer->tex);
			*layer = Layer();
		}
		g_width = g_height = 0;
		g_hasFrame = false;
	}

	bool create_layer(device *dev, Layer &layer, uint32_t w, uint32_t h, format fmt)
	{
		if (!dev->create_resource(
				resource_desc(w, h, 1, 1, fmt, 1, memory_heap::default_, resource_usage::shader_resource | resource_usage::copy_dest),
				nullptr, resource_usage::shader_resource, &layer.tex))
			return false;
		return dev->create_resource_view(layer.tex, resource_usage::shader_resource, resource_view_desc(fmt), &layer.srv);
	}

	void bind(effect_runtime *runtime)
	{
		runtime->update_texture_bindings("VCWORLD", g_world.srv, g_world.srv);
		runtime->update_texture_bindings("VCDEPTH", g_depth.srv, g_depth.srv);
		runtime->update_texture_bindings("VCOVERLAY", g_overlay.srv, g_overlay.srv);
	}

	/// Upload the newest published Minecraft frame, if there is one we haven't shown yet.
	void upload(effect_runtime *runtime)
	{
		const int64_t published = read<int64_t>(g_frameView + 32);
		if (published == g_lastPublish)
			return;
		const int32_t slot = read<int32_t>(g_frameView + 40);
		if (slot < 0 || slot >= g_slots)
			return;
		const uint8_t *desc = g_frameView + kSlotDesc + kSlotDescBytes * slot;
		const int64_t seq = read<int64_t>(desc);
		if (seq & 1)
			return;
		const uint32_t w = read<uint32_t>(desc + 24), h = read<uint32_t>(desc + 28);
		if (w == 0 || h == 0)
			return;
		device *dev = runtime->get_device();
		if (w != g_width || h != g_height)
		{
			destroy_layers(dev);
			if (!create_layer(dev, g_world, w, h, format::r8g8b8a8_unorm) ||
				!create_layer(dev, g_depth, w, h, format::r32_float) ||
				!create_layer(dev, g_overlay, w, h, format::r8g8b8a8_unorm))
			{
				destroy_layers(dev);
				return;
			}
			g_width = w;
			g_height = h;
			bind(runtime);
		}
		const uint8_t *base = g_frameView + kHeader + g_stride * slot;
		const size_t layer = size_t(w) * h * 4;
		subresource_data data;
		data.row_pitch = w * 4;
		data.slice_pitch = static_cast<uint32_t>(layer);
		data.data = const_cast<uint8_t *>(base);
		dev->update_texture_region(data, g_world.tex, 0);
		data.data = const_cast<uint8_t *>(base + layer);
		dev->update_texture_region(data, g_depth.tex, 0);
		data.data = const_cast<uint8_t *>(base + 2 * layer);
		dev->update_texture_region(data, g_overlay.tex, 0);
		if (read<int64_t>(desc) != seq)
			return; // Minecraft rewrote the slot mid-copy: show the next one instead
		g_lastPublish = published;
		g_mcNear = read<float>(desc + 32);
		g_mcFar = read<float>(desc + 36);
		g_mcFlags = read<int32_t>(desc + 44);
		g_mcPose.fov = read<float>(desc + 40);
		g_mcPose.yaw = read<float>(desc + 72);
		g_mcPose.pitch = read<float>(desc + 76);
		g_mcPose.roll = read<float>(desc + 80);
		g_mcPose.x = read<double>(desc + 48);
		g_mcPose.y = read<double>(desc + 56);
		g_mcPose.z = read<double>(desc + 64);
		g_mcPose.frame = read<int64_t>(desc + 16);
		g_mcPose.valid = true;
		g_hasFrame = true;
	}

	/// Camera-to-world rotation, as Minecraft builds it: rotationYXZ(pi - yaw, -pitch, roll) (camera looks down -z).
	void camera_rotation(const Pose &p, float m[3][3])
	{
		const float d2r = 3.14159265f / 180.0f;
		const float a = 3.14159265f - p.yaw * d2r, b = -p.pitch * d2r, c = p.roll * d2r;
		const float ca = std::cos(a), sa = std::sin(a), cb = std::cos(b), sb = std::sin(b), cc = std::cos(c), sc = std::sin(c);
		const float ry[3][3] = {{ca, 0, sa}, {0, 1, 0}, {-sa, 0, ca}};
		const float rx[3][3] = {{1, 0, 0}, {0, cb, -sb}, {0, sb, cb}};
		const float rz[3][3] = {{cc, -sc, 0}, {sc, cc, 0}, {0, 0, 1}};
		float t[3][3] = {};
		for (int i = 0; i < 3; ++i)
			for (int j = 0; j < 3; ++j)
				for (int k = 0; k < 3; ++k)
					t[i][j] += ry[i][k] * rx[k][j];
		for (int i = 0; i < 3; ++i)
			for (int j = 0; j < 3; ++j)
			{
				m[i][j] = 0;
				for (int k = 0; k < 3; ++k)
					m[i][j] += t[i][k] * rz[k][j];
			}
	}

	/// Rows of R_mc^T * R_host: turns a ray in Valheim's camera space into Minecraft's camera space.
	void warp_matrix(const Pose &host, const Pose &mc, float out[3][3])
	{
		float rh[3][3], rm[3][3];
		camera_rotation(host, rh);
		camera_rotation(mc, rm);
		for (int i = 0; i < 3; ++i)
			for (int j = 0; j < 3; ++j)
			{
				out[i][j] = 0;
				for (int k = 0; k < 3; ++k)
					out[i][j] += rm[k][i] * rh[k][j];
			}
	}

	/// Reload MineHeim.fx when the file changes (ReShade doesn't watch it), so shader tweaks need no game restart.
	void watch_effect_file(effect_runtime *runtime)
	{
		static DWORD next = 0;
		static FILETIME last = {};
		static wchar_t path[MAX_PATH] = {};
		if (GetTickCount() < next)
			return;
		next = GetTickCount() + 1000;
		if (path[0] == 0)
		{
			GetModuleFileNameW(nullptr, path, MAX_PATH);
			wchar_t *slash = wcsrchr(path, L'\\');
			if (slash)
				wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - path), L"reshade-shaders\\Shaders\\MineHeim.fx");
		}
		WIN32_FILE_ATTRIBUTE_DATA info;
		if (!GetFileAttributesExW(path, GetFileExInfoStandard, &info))
			return;
		if (last.dwLowDateTime != 0 && CompareFileTime(&info.ftLastWriteTime, &last) != 0)
			runtime->reload_effect_next_frame(kEffect);
		last = info.ftLastWriteTime;
	}

	void on_present(effect_runtime *runtime)
	{
		watch_effect_file(runtime); // every frame, even when the effect failed to compile
	}

	void set3(effect_runtime *runtime, const char *name, float a, float b, float c)
	{
		if (const effect_uniform_variable v = runtime->find_uniform_variable(kEffect, name); v.handle != 0)
			runtime->set_uniform_value_float(v, a, b, c);
	}

	/// Diagnostics: does the effect hold the values we set? (name found, value read back)
	void uniform_report(effect_runtime *runtime, char *dst, size_t cap)
	{
		float wt[3] = {-9, -9, -9}, tan3[3] = {-9, -9, -9}, planes[3] = {-9, -9, -9};
		float rep = -9.0f;
		const effect_uniform_variable vt = runtime->find_uniform_variable(kEffect, "WarpT");
		const effect_uniform_variable vtan = runtime->find_uniform_variable(kEffect, "WarpTan");
		const effect_uniform_variable vpl = runtime->find_uniform_variable(kEffect, "McPlanes");
		const effect_uniform_variable vre = runtime->find_uniform_variable(kEffect, "Reproject");
		if (vt.handle != 0)
			runtime->get_uniform_value_float(vt, wt, 3);
		if (vtan.handle != 0)
			runtime->get_uniform_value_float(vtan, tan3, 3);
		if (vpl.handle != 0)
			runtime->get_uniform_value_float(vpl, planes, 3);
		if (vre.handle != 0)
		{
			bool b = false;
			runtime->get_uniform_value_bool(vre, &b, 1);
			rep = b ? 1.0f : 0.0f;
		}
		std::snprintf(dst, cap, "uniforms: WarpT[%s]=%.4f %.4f %.4f WarpTan[%s]=%.4f %.4f %.4f McPlanes[%s]=%.3f %.1f %.0f Reproject[%s]=%.0f",
			vt.handle ? "ok" : "MISSING", wt[0], wt[1], wt[2], vtan.handle ? "ok" : "MISSING", tan3[0], tan3[1], tan3[2],
			vpl.handle ? "ok" : "MISSING", planes[0], planes[1], planes[2], vre.handle ? "ok" : "MISSING", rep);
	}

	void on_begin_effects(effect_runtime *runtime, command_list *, resource_view, resource_view)
	{
		bool on = open_mappings();
		Pose host;
		if (on)
		{
			// the pose of the frame being drawn (the render-thread marker), else the newest
			on = read_pose(g_renderFrame.load(std::memory_order_acquire), host) || read_pose(0, host);
		}
		bool fresh = false;
		if (g_poseView != nullptr)
		{
			// in a world (flag 1) and the plugin is still writing poses (menus and loading screens stop it)
			const int64_t written = read<int64_t>(g_poseView + 16);
			if (written != g_lastPoseWritten)
			{
				g_lastPoseWritten = written;
				g_poseChangedTick = GetTickCount();
			}
			fresh = GetTickCount() - g_poseChangedTick < 1000;
		}
		{
			Pose newest;
			if (read_pose(0, newest))
				g_newestPoseFrame = newest.frame;
		}
		if (on)
			on = (host.flags & 1) != 0 && fresh;
		if (GetTickCount() >= g_nextStatus)
		{
			g_nextStatus = GetTickCount() + 5000;
			char line[256];
			std::snprintf(line, sizeof(line), "MineHeim: mappings=%d pose=%d flags=%d fresh=%d hasFrame=%d renderFrame=%d markers=%d poseFrame=%lld newest=%lld mcFrame=%lld size=%ux%u -> active=%d",
				(g_frameView != nullptr && g_poseView != nullptr) ? 1 : 0, host.valid ? 1 : 0, host.flags, fresh ? 1 : 0, g_hasFrame ? 1 : 0,
				int(g_renderFrame.load()), int(g_markerCalls.load()), (long long)host.frame, (long long)g_newestPoseFrame.load(), (long long)g_mcPose.frame, g_width, g_height,
				(on && g_hasFrame) ? 1 : 0);
			reshade::log::message(reshade::log::level::info, line);
		}
		if (on)
			upload(runtime);
		on = on && g_hasFrame;
		g_usedActive = on;
		g_usedPoseFrame = host.frame;
		g_usedMcFrame = g_mcPose.frame;
		// The technique stays enabled (preset); McActive gates it, so Valheim passes through untouched until a
		// Minecraft frame is here. (Toggling techniques from inside this callback crashes ReShade.)
		if (const effect_uniform_variable v = runtime->find_uniform_variable(kEffect, "McActive"); v.handle != 0)
			runtime->set_uniform_value_bool(v, on);
		if (!on)
			return;
		set3(runtime, "McPlanes", g_mcNear, g_mcFar, float(g_mcFlags));
		set3(runtime, "FrameCode", float(host.frame & 0xFFFFFF), float(g_mcPose.frame & 0xFFFFFF), 0.0f);
		if (const effect_uniform_variable v = runtime->find_uniform_variable(kEffect, "HostPlanes"); v.handle != 0)
			runtime->set_uniform_value_float(v, host.nearClip, host.farClip);

		float predict = 0.0f;
		if (const effect_uniform_variable v = runtime->find_uniform_variable(kEffect, "PosePrediction"); v.handle != 0)
			runtime->get_uniform_value_float(v, &predict, 1);
		(void)predict; // the marker makes the pose exact; extrapolation is not needed
		const bool warp = g_mcPose.valid;
		float m[3][3] = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
		float t[3] = {0, 0, 0};
		if (warp)
		{
			warp_matrix(host, g_mcPose, m);
			// T = R_mc^T (host position - Minecraft's camera position), in Minecraft's camera space
			float rm[3][3];
			camera_rotation(g_mcPose, rm);
			const float d[3] = {float(host.x - g_mcPose.x), float(host.y - g_mcPose.y), float(host.z - g_mcPose.z)};
			for (int i = 0; i < 3; ++i)
				t[i] = rm[0][i] * d[0] + rm[1][i] * d[1] + rm[2][i] * d[2];
		}
		set3(runtime, "WarpRow0", m[0][0], m[0][1], m[0][2]);
		set3(runtime, "WarpRow1", m[1][0], m[1][1], m[1][2]);
		set3(runtime, "WarpRow2", m[2][0], m[2][1], m[2][2]);
		set3(runtime, "WarpT", t[0], t[1], t[2]);
		const float d2r = 3.14159265f / 180.0f;
		const float tanHost = std::tan(host.fov * d2r * 0.5f), tanMc = std::tan(g_mcPose.fov * d2r * 0.5f);
		set3(runtime, "WarpTan", tanHost, tanMc, float(g_width) / float(g_height));
		g_lastPoseFrame = int32_t(host.frame);
		if (GetTickCount() >= g_nextUniformReport)
		{
			g_nextUniformReport = GetTickCount() + 5000;
			char rep[512];
			uniform_report(runtime, rep, sizeof(rep));
			reshade::log::message(reshade::log::level::info, rep);
		}
		g_dbgHost[0] = host.x; g_dbgHost[1] = host.y; g_dbgHost[2] = host.z; g_dbgHost[3] = host.yaw; g_dbgHost[4] = host.pitch; g_dbgHost[5] = host.roll; g_dbgHost[6] = host.fov;
		g_dbgMc[0] = g_mcPose.x; g_dbgMc[1] = g_mcPose.y; g_dbgMc[2] = g_mcPose.z; g_dbgMc[3] = g_mcPose.yaw; g_dbgMc[4] = g_mcPose.pitch; g_dbgMc[5] = g_mcPose.roll; g_dbgMc[6] = g_mcPose.fov;
		for (int i = 0; i < 3; ++i)
		{
			g_dbgWarp[i * 3 + 0] = m[i][0]; g_dbgWarp[i * 3 + 1] = m[i][1]; g_dbgWarp[i * 3 + 2] = m[i][2];
			g_dbgWarp[9 + i] = t[i];
		}
	}

	/// After ReShade saved a screenshot: record next to it which Valheim pose and which Minecraft frame were composited
	/// into it (host/composite_oracle.py compares the picture with that pose's projection).
	void on_screenshot(effect_runtime *, const char *path)
	{
		char sidecar[1024];
		std::snprintf(sidecar, sizeof(sidecar), "%s.txt", path);
		if (FILE *f = std::fopen(sidecar, "w"))
		{
			std::fprintf(f, "host_frame=%lld\nmc_frame=%lld\nrender_frame=%d\nnewest_pose_frame=%lld\nactive=%d\nmarker_calls=%d\n",
				(long long)g_usedPoseFrame.load(), (long long)g_usedMcFrame.load(), int(g_renderFrame.load()),
				(long long)g_newestPoseFrame.load(), g_usedActive.load() ? 1 : 0, int(g_markerCalls.load()));
			std::fprintf(f, "host_pose=%.5f %.5f %.5f %.4f %.4f %.4f %.4f\nmc_pose=%.5f %.5f %.5f %.4f %.4f %.4f %.4f\nwarp_rows=%.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f\nwarp_t=%.6f %.6f %.6f\nmc_planes=%.4f %.4f %d\n",
				g_dbgHost[0], g_dbgHost[1], g_dbgHost[2], g_dbgHost[3], g_dbgHost[4], g_dbgHost[5], g_dbgHost[6],
				g_dbgMc[0], g_dbgMc[1], g_dbgMc[2], g_dbgMc[3], g_dbgMc[4], g_dbgMc[5], g_dbgMc[6],
				g_dbgWarp[0], g_dbgWarp[1], g_dbgWarp[2], g_dbgWarp[3], g_dbgWarp[4], g_dbgWarp[5], g_dbgWarp[6], g_dbgWarp[7], g_dbgWarp[8],
				g_dbgWarp[9], g_dbgWarp[10], g_dbgWarp[11], g_mcNear, g_mcFar, int(g_mcFlags));
			std::fclose(f);
		}
	}

	void on_reloaded_effects(effect_runtime *runtime)
	{
		if (g_width != 0)
			bind(runtime);
	}

	void on_destroy_effect_runtime(effect_runtime *runtime)
	{
		destroy_layers(runtime->get_device());
	}
}

// Queued by the plugin into the main camera's command stream (CommandBuffer.IssuePluginEvent): runs on Valheim's render
// thread exactly when that camera is drawn.
extern "C" __declspec(dllexport) void __stdcall VCRenderEvent(int eventId)
{
	g_renderFrame.store(eventId, std::memory_order_release);
	g_markerCalls.fetch_add(1, std::memory_order_relaxed);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
	switch (reason)
	{
	case DLL_PROCESS_ATTACH:
		if (!reshade::register_addon(module))
			return FALSE;
		QueryPerformanceFrequency(&g_qpcFreq);
		reshade::register_event<reshade::addon_event::reshade_begin_effects>(on_begin_effects);
		reshade::register_event<reshade::addon_event::reshade_present>(on_present);
		reshade::register_event<reshade::addon_event::reshade_reloaded_effects>(on_reloaded_effects);
		reshade::register_event<reshade::addon_event::reshade_screenshot>(on_screenshot);
		reshade::register_event<reshade::addon_event::destroy_effect_runtime>(on_destroy_effect_runtime);
		reshade::log::message(reshade::log::level::info, "MineHeim: add-on registered");
		break;
	case DLL_PROCESS_DETACH:
		reshade::unregister_addon(module);
		break;
	}
	return TRUE;
}
