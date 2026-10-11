package dev.daolyap.mineheim.client;

import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.mixin.RenderSetupAccessor;
import dev.daolyap.mineheim.client.mixin.RenderTypeAccessor;
import dev.daolyap.mineheim.client.mixin.SpriteContentsAccessor;
import java.io.IOException;
import java.io.InputStream;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.model.Model;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.renderer.OrderedSubmitNodeCollector;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.block.MovingBlockRenderState;
import net.minecraft.client.renderer.block.dispatch.BlockStateModelPart;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer;
import net.minecraft.client.renderer.gizmos.DrawableGizmoPrimitives;
import net.minecraft.client.renderer.item.ItemStackRenderState;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.QuadParticleRenderState;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.renderer.texture.UvMapping;
import net.minecraft.client.resources.model.geometry.BakedQuad;
import net.minecraft.client.resources.model.geometry.ItemQuads;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.Resource;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * Records what Minecraft would draw for an item (its ItemStackRenderState) instead of drawing it: the quads of a plain
 * item (a sword, a block, the extruded sprite Minecraft builds itself) and of a special one drawn with an entity model
 * (a shield, a trident), with their positions after every transform Minecraft applies, their texture coordinates and
 * their texture. The textures are packed into one small image, so the host gets one mesh and one texture per item.
 */
final class ItemCapture implements SubmitNodeCollector {
	/** One captured quad: 4 corners (x, y, z), texture coordinates (0..1, v down) into its source image, and the source. */
	private record Quad(float[] xyz, float[] uv, Source source) {
	}

	/** An image the quads are textured from (a sprite, or an entity texture), tinted (ARGB, -1 = none). */
	private record Source(String key, NativeImage image, int x, int y, int width, int height, int tint) {
	}

	/** The capture: quads as 4 corners each, xyz in the pose's space and uv into `png` (0..1, v down). */
	record Result(float[] xyz, float[] uv, byte[] png) {
	}

	private final List<Quad> quads = new ArrayList<>();
	private final Map<String, Source> sources = new LinkedHashMap<>();
	private final List<NativeImage> owned = new ArrayList<>();

	/** Capture an item as Minecraft draws it in `context`, after `pose` (render thread). Null if nothing is drawn. */
	static Result capture(final ItemStackRenderState state, final PoseStack pose) {
		ItemCapture c = new ItemCapture();
		try {
			state.submit(pose, c, 0xF000F0, 0, 0);
			return c.quads.isEmpty() ? null : c.pack();
		} catch (RuntimeException | IOException e) {
			MineHeim.LOG.warn("held item: cannot capture its model: {}", e.toString());
			return null;
		} finally {
			c.owned.forEach(NativeImage::close);
		}
	}

	// ------------------------------------------------------------------ what Minecraft submits

	@Override
	public void submitItem(final PoseStack pose, final ItemDisplayContext context, final int light, final int overlay, final int outline, final int[] tints,
		final ItemQuads itemQuads, final ItemStackRenderState.FoilType foil) {
		Matrix4f m = new Matrix4f(pose.last().pose());
		for (BakedQuad q : itemQuads.all()) {
			TextureAtlasSprite sprite = q.materialInfo().sprite();
			int tintIndex = q.materialInfo().tintIndex();
			int tint = tintIndex >= 0 && tints != null && tintIndex < tints.length ? tints[tintIndex] : -1;
			Source source = spriteSource(sprite, tint);
			if (source == null) {
				continue;
			}

			float[] xyz = new float[12], uv = new float[8];
			for (int i = 0; i < 4; i++) {
				Vector3f p = m.transformPosition(new Vector3f(q.position(i)));
				xyz[i * 3] = p.x;
				xyz[i * 3 + 1] = p.y;
				xyz[i * 3 + 2] = p.z;
				// atlas coordinates back to the sprite's own (0..1)
				long packed = q.packedUV(i);
				uv[i * 2] = (UVPair.unpackU(packed) - sprite.getU0()) / (sprite.getU1() - sprite.getU0());
				uv[i * 2 + 1] = (UVPair.unpackV(packed) - sprite.getV0()) / (sprite.getV1() - sprite.getV0());
			}

			quads.add(new Quad(xyz, uv, source));
		}
	}

	@Override
	@SuppressWarnings("unchecked")
	public <S> void submitModel(final Model<? super S> model, final S state, final PoseStack pose, final RenderType renderType, final int light, final int overlay,
		final int color, final UvMapping mapping, final int outline) {
		// a sprite-mapped model (the shield, banners) is textured from the sprite; else from the render type's own texture
		Source source = mapping instanceof TextureAtlasSprite sprite ? spriteSource(sprite, color == -1 ? -1 : color) : textureSource(renderType, color);
		if (source == null) {
			return;
		}

		((Model<S>) model).setupAnim(state);
		model.renderToBuffer(pose, new Recorder(source), light, overlay, color);
	}

	@Override
	public void submitCustomGeometry(final PoseStack pose, final RenderType renderType, final SubmitNodeCollector.CustomGeometryRenderer renderer) {
		Source source = textureSource(renderType, -1);
		if (source != null) {
			renderer.render(pose.last(), new Recorder(source));
		}
	}

	@Override
	public void submitBlockModel(final PoseStack pose, final RenderType renderType, final List<BlockStateModelPart> parts, final int[] tints, final int light,
		final int overlay, final int outline) {
		Matrix4f m = new Matrix4f(pose.last().pose());
		for (BlockStateModelPart part : parts) {
			for (net.minecraft.core.Direction side : directionsAndNull()) {
				for (BakedQuad q : part.getQuads(side)) {
					ItemQuads one = new ItemQuads(List.of(q), List.of(), List.of());
					PoseStack ps = new PoseStack();
					ps.last().pose().set(m);
					submitItem(ps, ItemDisplayContext.NONE, light, overlay, outline, tints, one, ItemStackRenderState.FoilType.NONE);
				}
			}
		}
	}

	private static net.minecraft.core.Direction[] directionsAndNull() {
		net.minecraft.core.Direction[] all = net.minecraft.core.Direction.values();
		net.minecraft.core.Direction[] out = new net.minecraft.core.Direction[all.length + 1];
		System.arraycopy(all, 0, out, 0, all.length);
		return out; // the last one stays null: the quads not culled by any side
	}

	/** Takes a model's vertices (already through the pose) four at a time. */
	private final class Recorder implements VertexConsumer {
		private final Source source;
		private final float[] xyz = new float[12], uv = new float[8];
		private int n = -1;

		Recorder(final Source source) {
			this.source = source;
		}

		@Override
		public VertexConsumer addVertex(final float x, final float y, final float z) {
			flush();
			n = n < 0 ? 0 : n + 1;
			xyz[n * 3] = x;
			xyz[n * 3 + 1] = y;
			xyz[n * 3 + 2] = z;
			return this;
		}

		@Override
		public VertexConsumer setUv(final float u, final float v) {
			if (n >= 0) {
				uv[n * 2] = u;
				uv[n * 2 + 1] = v;
				if (n == 3) {
					quads.add(new Quad(xyz.clone(), uv.clone(), source));
					n = -1;
				}
			}

			return this;
		}

		private void flush() {
			if (n >= 3) {
				n = -1;
			}
		}

		@Override
		public VertexConsumer setColor(final int r, final int g, final int b, final int a) {
			return this;
		}

		@Override
		public VertexConsumer setColor(final int argb) {
			return this;
		}

		@Override
		public VertexConsumer setUv1(final int u, final int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv2(final int u, final int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv3(final float u, final float v) {
			return this;
		}

		@Override
		public VertexConsumer setNormal(final float x, final float y, final float z) {
			return this;
		}

		@Override
		public VertexConsumer setLineWidth(final float width) {
			return this;
		}
	}

	// ------------------------------------------------------------------ textures

	private Source spriteSource(final TextureAtlasSprite sprite, final int tint) {
		String key = "sprite:" + sprite.contents().name() + "#" + tint;
		Source s = sources.get(key);
		if (s == null) {
			NativeImage image = ((SpriteContentsAccessor) sprite.contents()).mineheim$originalImage();
			if (image == null) {
				return null;
			}

			// an animated sprite (a clock, a compass) is its frames one under the other: the first frame
			s = new Source(key, image, 0, 0, sprite.contents().width(), sprite.contents().height(), tint);
			sources.put(key, s);
		}

		return s;
	}

	private static Method bindingLocation;

	private Source textureSource(final RenderType renderType, final int tint) {
		Identifier id = textureOf(renderType);
		if (id == null) {
			return null;
		}

		String key = "texture:" + id + "#" + tint;
		Source s = sources.get(key);
		if (s == null) {
			Optional<Resource> resource = Minecraft.getInstance().getResourceManager().getResource(id);
			if (resource.isEmpty()) {
				return null;
			}

			try (InputStream in = resource.get().open()) {
				NativeImage image = NativeImage.read(in);
				owned.add(image);
				s = new Source(key, image, 0, 0, image.getWidth(), image.getHeight(), tint);
				sources.put(key, s);
			} catch (IOException e) {
				return null;
			}
		}

		return s;
	}

	/** The render type's main texture ("Sampler0"), or its first. */
	private static Identifier textureOf(final RenderType renderType) {
		try {
			Map<String, ?> textures = ((RenderSetupAccessor) (Object) ((RenderTypeAccessor) renderType).mineheim$state()).mineheim$textures();
			Object binding = textures.containsKey("Sampler0") ? textures.get("Sampler0") : textures.values().stream().findFirst().orElse(null);
			if (binding == null) {
				return null;
			}

			if (bindingLocation == null) {
				bindingLocation = binding.getClass().getDeclaredMethod("location");
				bindingLocation.setAccessible(true);
			}

			return (Identifier) bindingLocation.invoke(binding);
		} catch (ReflectiveOperationException | RuntimeException e) {
			return null;
		}
	}

	/** All sources into one image (shelf packing, power-of-two width), the quads' uv moved into it. */
	private Result pack() throws IOException {
		List<Source> list = new ArrayList<>(sources.values());
		list.sort((a, b) -> Integer.compare(b.height(), a.height()));
		int area = 0, widest = 1;
		for (Source s : list) {
			area += s.width() * s.height();
			widest = Math.max(widest, s.width());
		}

		int width = Integer.highestOneBit(Math.max(widest, (int) Math.ceil(Math.sqrt(area))) * 2 - 1);
		Map<String, int[]> at = new LinkedHashMap<>();
		int x = 0, y = 0, row = 0;
		for (Source s : list) {
			if (x + s.width() > width) {
				x = 0;
				y += row;
				row = 0;
			}

			at.put(s.key(), new int[] {x, y});
			x += s.width();
			row = Math.max(row, s.height());
		}

		int height = Math.max(1, Integer.highestOneBit(Math.max(1, y + row) * 2 - 1));
		try (NativeImage out = new NativeImage(width, height, true)) {
			for (Source s : list) {
				int[] p = at.get(s.key());
				for (int j = 0; j < s.height(); j++) {
					for (int i = 0; i < s.width(); i++) {
						out.setPixel(p[0] + i, p[1] + j, tinted(s.image().getPixel(s.x() + i, s.y() + j), s.tint()));
					}
				}
			}

			float[] xyz = new float[quads.size() * 12], uv = new float[quads.size() * 8];
			for (int q = 0; q < quads.size(); q++) {
				Quad quad = quads.get(q);
				int[] p = at.get(quad.source().key());
				System.arraycopy(quad.xyz(), 0, xyz, q * 12, 12);
				for (int i = 0; i < 4; i++) {
					uv[q * 8 + i * 2] = (p[0] + quad.uv()[i * 2] * quad.source().width()) / width;
					uv[q * 8 + i * 2 + 1] = (p[1] + quad.uv()[i * 2 + 1] * quad.source().height()) / height;
				}
			}

			return new Result(xyz, uv, png(out));
		}
	}

	/** NativeImage has no in-memory PNG encoder: through a temporary file. */
	static byte[] png(final NativeImage image) throws IOException {
		java.nio.file.Path file = java.nio.file.Files.createTempFile("mineheim-item", ".png");
		try {
			image.writeToFile(file);
			return java.nio.file.Files.readAllBytes(file);
		} finally {
			java.nio.file.Files.deleteIfExists(file);
		}
	}

	/** ARGB pixel times an ARGB tint (-1: none), as Minecraft multiplies a tinted layer's colour. */
	private static int tinted(final int argb, final int tint) {
		if (tint == -1) {
			return argb;
		}

		int a = argb >>> 24, r = (argb >> 16 & 255) * (tint >> 16 & 255) / 255, g = (argb >> 8 & 255) * (tint >> 8 & 255) / 255, b = (argb & 255) * (tint & 255) / 255;
		return a << 24 | r << 16 | g << 8 | b;
	}

	// ------------------------------------------------------------------ everything else Minecraft might submit: not an item's shape

	@Override
	public OrderedSubmitNodeCollector order(final int order) {
		return this;
	}

	@Override
	public void submitShadow(final PoseStack pose, final float radius, final List<EntityRenderState.ShadowPiece> pieces) {
	}

	@Override
	public void submitNameTag(final PoseStack pose, final Vec3 at, final int y, final Component text, final boolean seeThrough, final int light, final CameraRenderState camera) {
	}

	@Override
	public void submitText(final PoseStack pose, final float x, final float y, final FormattedCharSequence text, final boolean shadow, final Font.DisplayMode mode,
		final int light, final int color, final int background, final int outline) {
	}

	@Override
	public void submitTextBackground(final PoseStack pose, final float x0, final float y0, final float x1, final float y1, final int color, final Font.DisplayMode mode,
		final int light) {
	}

	@Override
	public void submitFlame(final PoseStack pose, final EntityRenderState state, final Quaternionf rotation) {
	}

	@Override
	public void submitLeash(final PoseStack pose, final EntityRenderState.LeashState leash) {
	}

	@Override
	public <S> void submitCrumblingOverlay(final Model<? super S> model, final S state, final PoseStack pose, final RenderType renderType, final int light,
		final int overlay, final int color, final ModelFeatureRenderer.CrumblingOverlay crumbling) {
	}

	@Override
	public void submitMovingBlock(final PoseStack pose, final MovingBlockRenderState block, final int light) {
	}

	@Override
	public void submitBreakingBlockModel(final PoseStack pose, final List<BlockStateModelPart> parts, final int progress, final boolean b) {
	}

	@Override
	public void submitShapeOutline(final PoseStack pose, final VoxelShape shape, final RenderType renderType, final int color, final float width, final boolean b) {
	}

	@Override
	public void submitQuadParticleGroup(final QuadParticleRenderState particles) {
	}

	@Override
	public void submitGizmoPrimitives(final DrawableGizmoPrimitives.Group group, final CameraRenderState camera, final boolean b) {
	}
}
