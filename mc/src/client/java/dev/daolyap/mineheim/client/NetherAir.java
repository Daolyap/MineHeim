package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.NetherInfection;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.core.particles.SimpleParticleType;
import net.minecraft.util.RandomSource;
import net.minecraft.world.phys.Vec3;

/**
 * The air over a Nether infection: ash, crimson and warped spores and the odd ember drift around the camera while it is
 * over infected land, thicker towards the portal. Minecraft's own particles, so they reach the host's picture with
 * Minecraft's frame.
 */
final class NetherAir {
	private NetherAir() {
	}

	static void tick(final Minecraft minecraft) {
		ClientLevel level = minecraft.level;
		float[] zones = NetherInfection.zones;
		if (level == null || zones.length == 0 || !MineHeim.active || minecraft.isPaused()) {
			return;
		}

		Vec3 cam = minecraft.gameRenderer.mainCamera().position();
		float strength = 0f;
		for (int i = 0; i + 3 < zones.length; i += 4) {
			double dx = cam.x - zones[i], dz = cam.z - zones[i + 2], r = zones[i + 3];
			if (r < 1) {
				continue;
			}

			double d = Math.sqrt(dx * dx + dz * dz);
			// full inside, fading over the last 8 blocks of the edge and beyond it
			strength = Math.max(strength, (float) Math.min(1.0, Math.max(0.0, (r + 8 - d) / 16.0)) * (float) Math.min(1.0, 0.4 + r / 32.0));
		}

		if (strength <= 0f) {
			return;
		}

		RandomSource random = level.getRandom();
		int count = Math.round(strength * 10f);
		for (int k = 0; k < count; k++) {
			double x = cam.x + (random.nextDouble() - 0.5) * 24, y = cam.y + (random.nextDouble() - 0.4) * 12, z = cam.z + (random.nextDouble() - 0.5) * 24;
			int roll = random.nextInt(100);
			SimpleParticleType type = roll < 55 ? ParticleTypes.ASH : roll < 75 ? ParticleTypes.CRIMSON_SPORE : roll < 88 ? ParticleTypes.WARPED_SPORE
				: roll < 96 ? ParticleTypes.WHITE_ASH : ParticleTypes.LAVA;
			level.addParticle(type, x, y, z, 0, type == ParticleTypes.LAVA ? 0.0 : -0.01, 0);
		}
	}
}
