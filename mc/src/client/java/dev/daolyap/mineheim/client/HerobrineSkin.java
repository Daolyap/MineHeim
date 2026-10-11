package dev.daolyap.mineheim.client;

import com.mojang.blaze3d.platform.NativeImage;
import dev.daolyap.mineheim.MineHeim;
import java.io.IOException;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerSkin;

/**
 * Was it Herobrine all along? Steve's skin with white eyes (the host's Steve.HerobrineEyes), so Minecraft's own model
 * of him (the host's Steve.Avatar = Minecraft) matches the one the host draws.
 */
public final class HerobrineSkin {
	/** The classic skin's eye pixels (x, y): the face is (8..15, 8..15), the eyes are on row 12. */
	private static final int[] EYES = {9, 12, 10, 12, 13, 12, 14, 12};
	private static final Identifier ID = Identifier.fromNamespaceAndPath("mineheim", "herobrine_skin");
	private static final ClientAsset.Texture TEXTURE = new ClientAsset.ResourceTexture(ID, ID);

	/** The host wants his eyes on the skin Minecraft draws. */
	static volatile boolean wanted;
	private static volatile boolean ready;

	private HerobrineSkin() {
	}

	/** The skin Minecraft draws Steve in now (its PNG): make the white-eyed copy (render thread). */
	static void make(final byte[] png) {
		ready = false;
		if (!wanted || png == null) {
			return;
		}

		Minecraft minecraft = Minecraft.getInstance();
		minecraft.execute(() -> {
			try {
				NativeImage image = NativeImage.read(png);
				if (image.getWidth() != 64 || image.getHeight() != 64) {
					image.close(); // an old 64x32 skin: Minecraft converts those itself; keep its own eyes
					return;
				}

				for (int i = 0; i < EYES.length; i += 2) {
					image.setPixel(EYES[i], EYES[i + 1], 0xFFFFFFFF);
				}

				minecraft.getTextureManager().register(ID, new DynamicTexture(() -> "MineHeim Herobrine skin", image));
				ready = true;
			} catch (IOException | RuntimeException e) {
				MineHeim.LOG.warn("Herobrine's eyes: cannot read the skin: {}", e.toString());
			}
		});
	}

	/** `skin` with the white-eyed body, if there is one (same model, cape and elytra). */
	public static PlayerSkin apply(final PlayerSkin skin) {
		return wanted && ready && skin != null ? new PlayerSkin(TEXTURE, skin.cape(), skin.elytra(), skin.model(), skin.secure()) : skin;
	}
}
