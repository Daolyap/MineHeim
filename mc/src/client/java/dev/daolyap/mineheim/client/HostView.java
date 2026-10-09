package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.MineHeim;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.Blocks;

/**
 * What the host's player sees of this world besides the camera:
 * <ul>
 * <li>Steve: drawn (with his hand and Minecraft's HUD) only while the host is in Minecraft mode ({"t":"steve","on":bool}); his body
 * not at all when the host draws it itself ("body":false).</li>
 * <li>The host's exact ground ({"t":"heights","c":[x,z,cm,...]}): the barrier ground is whole blocks, up to 1 m under the host's
 * real surface, so mobs and items standing on it are drawn standing on the real surface instead.</li>
 * <li>The host process: when it is gone without saying goodbye (a crash, Task Manager), Minecraft saves and quits too.</li>
 * </ul>
 */
public final class HostView {
	/** Steve is shown (Minecraft mode on in the host). */
	public static volatile boolean steve = true;
	/** Steve's body is drawn here (false: the host draws it itself, in the same frame as its own player). */
	public static volatile boolean steveBody = true;
	/** Dress Steve in the account's own skin instead of classic Steve (the host's Steve.Skin = Account). */
	public static volatile boolean ownSkin = false;
	/** Host surface per column (key x,z), Minecraft y in centimetres. */
	private static final ConcurrentHashMap<Long, Integer> HEIGHTS = new ConcurrentHashMap<>();
	private static volatile long watchedHost = -1;

	private HostView() {
	}

	static void heights(final int[] c) {
		for (int i = 0; i + 2 < c.length; i += 3) {
			HEIGHTS.put(key(c[i], c[i + 1]), c[i + 2]);
		}

		if (HEIGHTS.size() > 120_000) {
			HEIGHTS.clear(); // far behind the player; the host sends what is around it again as it probes
		}
	}

	static void clearHeights() {
		HEIGHTS.clear();
	}

	private static long key(final int x, final int z) {
		return ((long) x << 32) | (z & 0xFFFFFFFFL);
	}

	/**
	 * Where a mob or item at (x, y, z) standing on the barrier ground should be drawn: on the host's surface there
	 * (interpolated between the column centres), or y unchanged if it isn't standing on host ground.
	 */
	public static double groundY(final double x, final double y, final double z) {
		if (HEIGHTS.isEmpty()) {
			return y;
		}

		ClientLevel level = Minecraft.getInstance().level;
		BlockPos below = BlockPos.containing(x, y - 0.05, z);
		if (level == null || !level.getBlockState(below).is(Blocks.BARRIER) || Math.abs(y - (below.getY() + 1)) > 0.05) {
			return y; // not resting on top of a barrier: a block, a slab, the air
		}

		// bilinear between the four column centres around (x, z); a missing column counts as the barrier's own top
		double fx = x - 0.5, fz = z - 0.5;
		int x0 = (int) Math.floor(fx), z0 = (int) Math.floor(fz);
		double tx = fx - x0, tz = fz - z0;
		double sum = 0.0, weight = 0.0;
		for (int dx = 0; dx <= 1; dx++) {
			for (int dz = 0; dz <= 1; dz++) {
				Integer cm = HEIGHTS.get(key(x0 + dx, z0 + dz));
				if (cm == null) {
					continue;
				}

				double w = (dx == 0 ? 1.0 - tx : tx) * (dz == 0 ? 1.0 - tz : tz);
				sum += cm / 100.0 * w;
				weight += w;
			}
		}

		if (weight < 1e-3) {
			return y;
		}

		double surface = sum / weight;
		// only ever lifted out of the barrier (never sunk), and only by less than a block
		return surface > y && surface - y < 1.25 ? surface : y;
	}

	/** {"t":"window",...,"host":pid,"quitWithHost":bool}: follow the host process (once per process). */
	static void watchHost(final long pid, final boolean quitWithHost) {
		if (!quitWithHost || pid <= 0 || pid == watchedHost) {
			return;
		}

		watchedHost = pid;
		// a plain polling thread: ProcessHandle.onExit() of a process that is not our child overflows the JDK's small
		// "process reaper" stack (seen in the real game)
		Thread t = new Thread(() -> {
			while (watchedHost == pid) {
				try {
					Thread.sleep(2000L);
				} catch (InterruptedException e) {
					return;
				}

				if (!ProcessHandle.of(pid).map(ProcessHandle::isAlive).orElse(false)) {
					if (watchedHost == pid) {
						MineHeim.LOG.info("the host (pid {}) is gone: saving and quitting Minecraft", pid);
						Minecraft minecraft = Minecraft.getInstance();
						minecraft.execute(minecraft::stop);
						quitWatchdog();
					}

					return;
				}
			}
		}, "mineheim-host-watch");
		t.setDaemon(true);
		t.start();
		MineHeim.LOG.info("following the host process {}: Minecraft quits when it goes", pid);
	}

	private static volatile boolean watchdog;

	/**
	 * Minecraft was asked to quit: if the process is still alive a minute later (a thread that never ends, a stuck
	 * save), end it. A normal quit saves and exits in a few seconds and never gets here.
	 */
	static void quitWatchdog() {
		if (watchdog) {
			return;
		}

		watchdog = true;
		Thread t = new Thread(() -> {
			try {
				Thread.sleep(60_000L);
			} catch (InterruptedException e) {
				return;
			}

			MineHeim.LOG.warn("Minecraft did not exit within 60 s of quitting: ending the process");
			Runtime.getRuntime().halt(0);
		}, "mineheim-quit-watchdog");
		t.setDaemon(true);
		t.start();
	}
}
