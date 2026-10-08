package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.NetherInfection;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.resources.sounds.AbstractTickableSoundInstance;
import net.minecraft.client.resources.sounds.SimpleSoundInstance;
import net.minecraft.client.resources.sounds.SoundInstance;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.core.particles.SimpleParticleType;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.phys.Vec3;

/**
 * The air and the sound over an infection, around the camera, thicker towards the portal:
 * <ul>
 * <li>the Nether: ash, crimson and warped spores and embers; the Nether wastes' drone, with its moans and the crimson
 * forest's and soul sand valley's sounds now and then</li>
 * <li>the End: portal motes drifting up and the odd end rod spark; a low portal hum and an enderman's voice far off</li>
 * </ul>
 * Minecraft's own particles and sounds: the particles reach the host's picture with Minecraft's frame, the sounds play
 * from Minecraft (which keeps its sound while its window is parked).
 */
final class NetherAir {
	private static final float[] strength = new float[2];
	private static final Loop[] loops = new Loop[2];
	private static int additionIn = 80;

	private NetherAir() {
	}

	static void tick(final Minecraft minecraft) {
		ClientLevel level = minecraft.level;
		float[] zones = NetherInfection.zones;
		strength[0] = strength[1] = 0f;
		if (level != null && zones.length > 0 && MineHeim.active && !minecraft.isPaused()) {
			Vec3 cam = minecraft.gameRenderer.mainCamera().position();
			for (int i = 0; i + 4 < zones.length; i += 5) {
				double dx = cam.x - zones[i], dz = cam.z - zones[i + 2], r = zones[i + 3];
				if (r < 1) {
					continue;
				}

				double d = Math.sqrt(dx * dx + dz * dz);
				// full inside, fading over the last 8 blocks of the edge and beyond it; a small infection is fainter
				float s = (float) Math.min(1.0, Math.max(0.0, (r + 8 - d) / 16.0)) * (float) Math.min(1.0, 0.4 + r / 32.0);
				int kind = (int) zones[i + 4] == 1 ? 1 : 0;
				strength[kind] = Math.max(strength[kind], s);
			}
		}

		if (level != null) {
			particles(level, minecraft);
		}

		sounds(minecraft);
	}

	private static void particles(final ClientLevel level, final Minecraft minecraft) {
		if (strength[0] <= 0f && strength[1] <= 0f) {
			return;
		}

		Vec3 cam = minecraft.gameRenderer.mainCamera().position();
		RandomSource random = level.getRandom();
		for (int kind = 0; kind < 2; kind++) {
			int count = Math.round(strength[kind] * 10f);
			for (int k = 0; k < count; k++) {
				double x = cam.x + (random.nextDouble() - 0.5) * 24, y = cam.y + (random.nextDouble() - 0.4) * 12, z = cam.z + (random.nextDouble() - 0.5) * 24;
				int roll = random.nextInt(100);
				if (kind == 0) {
					SimpleParticleType type = roll < 55 ? ParticleTypes.ASH : roll < 75 ? ParticleTypes.CRIMSON_SPORE : roll < 88 ? ParticleTypes.WARPED_SPORE
						: roll < 96 ? ParticleTypes.WHITE_ASH : ParticleTypes.LAVA;
					level.addParticle(type, x, y, z, 0, type == ParticleTypes.LAVA ? 0.0 : -0.01, 0);
				} else if (roll < 70) {
					level.addParticle(ParticleTypes.PORTAL, x, y - 1.5, z, (random.nextDouble() - 0.5) * 0.4, random.nextDouble() * 0.6, (random.nextDouble() - 0.5) * 0.4);
				} else if (roll < 92) {
					level.addParticle(ParticleTypes.REVERSE_PORTAL, x, y, z, 0, 0.02, 0);
				} else {
					level.addParticle(ParticleTypes.END_ROD, x, y, z, 0, 0.01, 0);
				}
			}
		}
	}

	private static void sounds(final Minecraft minecraft) {
		for (int kind = 0; kind < 2; kind++) {
			if (strength[kind] > 0f && (loops[kind] == null || loops[kind].isStopped())) {
				loops[kind] = new Loop(kind == 0 ? SoundEvents.AMBIENT_NETHER_WASTES_LOOP.value() : SoundEvents.PORTAL_AMBIENT, kind);
				minecraft.getSoundManager().play(loops[kind]);
				MineHeim.LOG.info("nether air: {} ambience started", kind == 0 ? "nether" : "end");
			}
		}

		float s = Math.max(strength[0], strength[1]);
		if (s <= 0f || --additionIn > 0) {
			return;
		}

		RandomSource random = RandomSource.create();
		additionIn = 60 + random.nextInt(140);
		SoundEvent sound;
		if (strength[0] >= strength[1]) {
			int roll = random.nextInt(3);
			sound = (roll == 0 ? SoundEvents.AMBIENT_NETHER_WASTES_ADDITIONS : roll == 1 ? SoundEvents.AMBIENT_CRIMSON_FOREST_ADDITIONS
				: SoundEvents.AMBIENT_SOUL_SAND_VALLEY_ADDITIONS).value();
		} else {
			sound = random.nextBoolean() ? SoundEvents.ENDERMAN_AMBIENT : SoundEvents.AMBIENT_WARPED_FOREST_ADDITIONS.value();
		}

		minecraft.getSoundManager().play(new SimpleSoundInstance(sound.location(), SoundSource.AMBIENT, 0.5f * s, 0.8f + random.nextFloat() * 0.3f, random,
			false, 0, SoundInstance.Attenuation.NONE, 0.0, 0.0, 0.0, true));
	}

	/** An ambient loop that follows the camera and fades with the infection's strength (and stops when it is gone). */
	private static final class Loop extends AbstractTickableSoundInstance {
		private final int kind;
		private float level;

		Loop(final SoundEvent sound, final int kind) {
			super(sound, SoundSource.AMBIENT, RandomSource.create());
			this.kind = kind;
			this.looping = true;
			this.delay = 0;
			this.volume = 0.01f;
			this.pitch = kind == 1 ? 0.55f : 1.0f;
			this.relative = true;
			this.attenuation = SoundInstance.Attenuation.NONE;
		}

		@Override
		public void tick() {
			float target = strength[this.kind] * (this.kind == 1 ? 0.35f : 0.8f);
			this.level += Math.signum(target - this.level) * Math.min(Math.abs(target - this.level), 0.02f);
			this.volume = Math.max(0.0001f, this.level);
			if (target <= 0f && this.level <= 0.001f) {
				this.stop();
			}
		}
	}
}
