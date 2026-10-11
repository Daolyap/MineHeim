package dev.daolyap.mineheim.client;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.math.Axis;
import com.mojang.serialization.JsonOps;
import dev.daolyap.mineheim.MineHeim;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.renderer.item.ItemStackRenderState;
import net.minecraft.client.resources.model.EquipmentClientInfo;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.Resource;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.HumanoidArm;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.DyedItemColor;
import net.minecraft.world.item.equipment.Equippable;

/**
 * What Steve holds and wears, for the host that draws him itself (its Steve.Avatar = Native). Whenever a hand's item or an
 * armour piece changes (and when the host asks, {"t":"heldreq"}), Minecraft's own model of it goes to the host:
 * <ul>
 * <li>{"t":"held","hand":"main|off","item":id,"v":[x,y,z,...],"uv":[u,v,...],"png":base64}: the item exactly as Minecraft draws
 * it in that hand (its model and display transform, a flat item's extruded sprite, a shield's entity model), as quads (4
 * corners each) in the arm's own space (blocks, Minecraft's model axes), textured from one small packed image (uv 0..1, v down).
 * An empty hand: "item":"" and nothing else.</li>
 * <li>{"t":"armor","slot":"head|chest|legs|feet","item":id,"layer":"humanoid|humanoid_leggings","png":base64}: the armour piece's
 * texture (its layers, dyed as Minecraft dyes them, in one image); the host puts it on Steve's own boxes, grown as
 * Minecraft grows armour. Nothing worn: "item":"".</li>
 * </ul>
 */
public final class HeldItems {
	private static final EquipmentSlot[] ARMOUR = {EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET};
	private static final String[] ARMOUR_NAMES = {"head", "chest", "legs", "feet"};
	private static final int[] sent = new int[6];
	private static volatile boolean resend = true;

	private HeldItems() {
	}

	/** {"t":"heldreq"}: the host (re)connected or wants it all again. */
	static void request() {
		resend = true;
	}

	/** Every client tick (render thread). */
	static void tick(final Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (player == null || !MineHeim.active) {
			return;
		}

		boolean all = resend;
		resend = false;
		ItemStack main = player.getMainHandItem(), off = player.getOffhandItem();
		boolean rightHanded = player.getMainArm() == HumanoidArm.RIGHT;
		if (changed(0, main, all)) {
			sendHeld(minecraft, player, "main", main, rightHanded ? HumanoidArm.RIGHT : HumanoidArm.LEFT);
		}

		if (changed(1, off, all)) {
			sendHeld(minecraft, player, "off", off, rightHanded ? HumanoidArm.LEFT : HumanoidArm.RIGHT);
		}

		for (int i = 0; i < ARMOUR.length; i++) {
			ItemStack worn = player.getItemBySlot(ARMOUR[i]);
			if (changed(2 + i, worn, all)) {
				sendArmour(minecraft, ARMOUR_NAMES[i], ARMOUR[i], worn);
			}
		}
	}

	private static boolean changed(final int slot, final ItemStack stack, final boolean all) {
		int hash = stack.isEmpty() ? 0 : 31 * ItemStack.hashItemAndComponents(stack) + 1;
		if (!all && hash == sent[slot]) {
			return false;
		}

		sent[slot] = hash;
		return true;
	}

	private static String id(final ItemStack stack) {
		return stack.isEmpty() ? "" : BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath();
	}

	// ------------------------------------------------------------------ held items

	private static void sendHeld(final Minecraft minecraft, final LocalPlayer player, final String hand, final ItemStack stack, final HumanoidArm arm) {
		ItemCapture.Result r = null;
		String valheim = ValheimBag.standIn(stack);
		if (!valheim.isEmpty()) {
			// a Valheim item's stand-in: the host shows the real item (the Viking holds it), nothing of Minecraft's
			MineHeim.events.accept("{\"t\":\"held\",\"hand\":\"" + hand + "\",\"arm\":\"" + (arm == HumanoidArm.LEFT ? "left" : "right") + "\",\"item\":\"\",\"valheim\":\""
				+ valheim.replace("\"", "") + "\"}");
			return;
		}

		if (!stack.isEmpty()) {
			boolean left = arm == HumanoidArm.LEFT;
			ItemStackRenderState state = new ItemStackRenderState();
			minecraft.getItemModelResolver().updateForLiving(state, stack, left ? ItemDisplayContext.THIRD_PERSON_LEFT_HAND : ItemDisplayContext.THIRD_PERSON_RIGHT_HAND, player);
			// ItemInHandLayer, from the arm's own space on: the item lies along the arm, at its hand
			PoseStack pose = new PoseStack();
			pose.rotateDegrees(Axis.XP, -90.0F);
			pose.rotateDegrees(Axis.YP, 180.0F);
			pose.translate((left ? -1.0F : 1.0F) / 16.0F, 2.0F / 16.0F, -10.0F / 16.0F);
			r = ItemCapture.capture(state, pose);
		}

		StringBuilder b = new StringBuilder(r == null ? 64 : 64 + r.xyz().length * 9 + r.uv().length * 7 + r.png().length * 4 / 3);
		b.append("{\"t\":\"held\",\"hand\":\"").append(hand).append("\",\"arm\":\"").append(arm == HumanoidArm.LEFT ? "left" : "right")
			.append("\",\"item\":\"").append(r == null ? "" : id(stack)).append('"');
		if (r != null) {
			b.append(",\"v\":");
			numbers(b, r.xyz(), "%.4f");
			b.append(",\"uv\":");
			numbers(b, r.uv(), "%.5f");
			b.append(",\"png\":\"").append(Base64.getEncoder().encodeToString(r.png())).append('"');
		}

		MineHeim.events.accept(b.append('}').toString());
		MineHeim.LOG.info("held item for the host: {} hand {} ({} quads)", hand, stack.isEmpty() ? "empty" : id(stack), r == null ? 0 : r.xyz().length / 12);
	}

	private static void numbers(final StringBuilder b, final float[] values, final String format) {
		b.append('[');
		for (int i = 0; i < values.length; i++) {
			b.append(i > 0 ? "," : "").append(String.format(Locale.ROOT, format, values[i]));
		}

		b.append(']');
	}

	// ------------------------------------------------------------------ armour

	private static void sendArmour(final Minecraft minecraft, final String slot, final EquipmentSlot equipmentSlot, final ItemStack stack) {
		Equippable equippable = stack.isEmpty() ? null : stack.get(DataComponents.EQUIPPABLE);
		byte[] png = null;
		EquipmentClientInfo.LayerType type = equipmentSlot == EquipmentSlot.LEGS ? EquipmentClientInfo.LayerType.HUMANOID_LEGGINGS : EquipmentClientInfo.LayerType.HUMANOID;
		if (equippable != null && equippable.slot() == equipmentSlot && equippable.assetId().isPresent()) {
			png = armourTexture(minecraft, equippable.assetId().get().identifier(), type, stack);
		}

		JsonObject m = new JsonObject();
		m.addProperty("t", "armor");
		m.addProperty("slot", slot);
		m.addProperty("item", png == null ? "" : id(stack));
		if (png != null) {
			m.addProperty("layer", type == EquipmentClientInfo.LayerType.HUMANOID_LEGGINGS ? "humanoid_leggings" : "humanoid");
			m.addProperty("png", Base64.getEncoder().encodeToString(png));
		}

		MineHeim.events.accept(m.toString());
		MineHeim.LOG.info("armour for the host: {} {}", slot, png == null ? "none" : id(stack));
	}

	/**
	 * The armour's layers for `type` (Minecraft's equipment/<asset>.json), one over the other in one image, each dyed as
	 * Minecraft dyes it (leather: the item's colour, or leather's own brown). Null if it has none (an elytra, a carved pumpkin).
	 */
	private static byte[] armourTexture(final Minecraft minecraft, final Identifier asset, final EquipmentClientInfo.LayerType type, final ItemStack stack) {
		Identifier json = Identifier.fromNamespaceAndPath(asset.getNamespace(), "equipment/" + asset.getPath() + ".json");
		Optional<Resource> resource = minecraft.getResourceManager().getResource(json);
		if (resource.isEmpty()) {
			return null;
		}

		try (Reader reader = new InputStreamReader(resource.get().open(), StandardCharsets.UTF_8)) {
			JsonElement parsed = JsonParser.parseReader(reader);
			EquipmentClientInfo info = EquipmentClientInfo.CODEC.parse(JsonOps.INSTANCE, parsed).result().orElse(null);
			List<EquipmentClientInfo.Layer> layers = info == null ? List.of() : info.getLayers(type);
			if (layers.isEmpty()) {
				return null;
			}

			DyedItemColor dyed = stack.get(DataComponents.DYED_COLOR);
			NativeImage out = null;
			try {
				for (EquipmentClientInfo.Layer layer : layers) {
					int tint = -1;
					if (layer.dyeable().isPresent()) {
						Optional<Integer> undyed = layer.dyeable().get().colorWhenUndyed();
						if (dyed == null && undyed.isEmpty()) {
							continue; // only drawn when dyed
						}

						tint = 0xFF000000 | (dyed != null ? dyed.rgb() : undyed.get());
					}

					Optional<Resource> texture = minecraft.getResourceManager().getResource(layer.getTextureLocation(type));
					if (texture.isEmpty()) {
						continue;
					}

					try (InputStream in = texture.get().open(); NativeImage image = NativeImage.read(in)) {
						if (out == null) {
							out = new NativeImage(image.getWidth(), image.getHeight(), true);
						}

						over(out, image, tint);
					}
				}

				return out == null ? null : ItemCapture.png(out);
			} finally {
				if (out != null) {
					out.close();
				}
			}
		} catch (IOException | RuntimeException e) {
			MineHeim.LOG.warn("armour texture for {}: {}", asset, e.toString());
			return null;
		}
	}

	/** `layer` (tinted, ARGB; -1 = none) drawn over `out` where it is opaque. */
	private static void over(final NativeImage out, final NativeImage layer, final int tint) {
		int w = Math.min(out.getWidth(), layer.getWidth()), h = Math.min(out.getHeight(), layer.getHeight());
		for (int y = 0; y < h; y++) {
			for (int x = 0; x < w; x++) {
				int p = layer.getPixel(x, y);
				if ((p >>> 24) < 128) {
					continue;
				}

				if (tint != -1) {
					p = 0xFF000000 | ((p >> 16 & 255) * (tint >> 16 & 255) / 255) << 16 | ((p >> 8 & 255) * (tint >> 8 & 255) / 255) << 8 | (p & 255) * (tint & 255) / 255;
				}

				out.setPixel(x, y, p | 0xFF000000);
			}
		}
	}
}
