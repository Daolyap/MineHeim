"""One-off port helper: derives compositor/MineHeim.fx from the GTA example's MCPassthrough.fx (MIT, rehan-remade/
universal-modder). Kept so the changes are visible: run it from the repo root with the toolkit clone next to the repo.
After the port, edit MineHeim.fx directly (this script is not part of the build).
"""
from pathlib import Path

SRC = Path(__file__).resolve().parents[2] / "universal-modder" / "examples" / "minecraft-gta5-passthrough" / "gta" / "shaders" / "MCPassthrough.fx"
DST = Path(__file__).resolve().parent / "MineHeim.fx"

s = SRC.read_text(encoding="utf-8")

# header
a = s.index('#include "ReShade.fxh"')
header = """// Composites Minecraft into Valheim (Direct3D 11). The MineHeim add-on (MineHeim.addon64) uploads Minecraft's
// latest frame into the VCWORLD / VCDEPTH / VCOVERLAY textures and sets the plane and pose uniforms.
//
// Ported from the GTA V example of rehan-remade/universal-modder (MIT): the effect is the same, with Valheim's depth
// conventions (reversed Z, and the depth buffer arrives upside down: RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1).
//
// world: premultiplied RGBA, drawn where Minecraft is nearer than Valheim's depth buffer (both in metres).
// overlay: Minecraft's hand, HUD and screens, always on top.
"""
s = header + s[a:]

s = s.replace("MCWORLD", "VCWORLD").replace("MCDEPTH", "VCDEPTH").replace("MCOVERLAY", "VCOVERLAY")
s = s.replace("technique MCPassthrough", "technique MineHeim")
s = s.replace("GTA's", "Valheim's").replace("GTA", "Valheim")
s = s.replace("Valheim's own grading does to its world", "the game's own grading does to its world")

# Valheim's depth buffer is upside down in ReShade's view: sample it through one helper that honours the define
helper = """
// ReShade hands over Valheim's depth buffer upside down (checked with DisplayDepth.fx in gameplay); the define flips it back.
float host_depth_raw(float2 uv)
{
#if RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN
	uv.y = 1.0 - uv.y;
#endif
	return tex2Dlod(ReShade::DepthBuffer, float4(uv, 0, 0)).x;
}

float luma(float3 c)"""
assert "float luma(float3 c)" in s
s = s.replace("\nfloat luma(float3 c)", helper, 1)
old = "const float zh = host_linear(tex2Dlod(ReShade::DepthBuffer, float4(uv, 0, 0)).x);"
assert old in s
s = s.replace(old, "const float zh = host_linear(host_depth_raw(uv));")
DST.write_text(s, encoding="utf-8")
print("wrote", DST, len(s), "bytes")
