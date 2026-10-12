package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.MineHeim;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.stream.Stream;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;

/**
 * Each of the host's worlds has its own Minecraft world: what Steve builds in one Valheim world stays in that world.
 * The host names its world ({"t":"vworld","name":..,"id":..}) whenever its player enters one; Minecraft opens the matching
 * save ("mineheim-<name>-<id>"), creating it the first time, and switches to it (saving the open one) if another is open.
 * The first world the host names takes over the old shared save ("mineheim"), so nothing built before is lost.
 * Without a host (a test client), the shared save opens as before.
 */
final class WorldSwitch {
	/** The shared save of earlier versions, and the one a test client without a host opens. */
	static final String LEGACY = "mineheim";
	/** How long the title screen waits for a host before opening the shared save (ticks). */
	private static final int NO_HOST_TICKS = 400;

	/** The save the host wants ("" = not said yet), and the one open or opening. */
	private static volatile String wanted = "";
	private static String open = "";
	private static boolean switching;
	private static int titleTicks;

	private WorldSwitch() {
	}

	/** {"t":"vworld","name":..,"id":..} (any thread). */
	static void host(final String name, final String id) {
		String folder = folder(name, id);
		if (!folder.equals(wanted)) {
			MineHeim.LOG.info("the host is in world {} ({}): Minecraft world {}", name, id, folder);
			wanted = folder;
		}
	}

	static String folder(final String name, final String id) {
		String clean = name.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9_-]", "_");
		if (clean.length() > 32) {
			clean = clean.substring(0, 32);
		}

		return LEGACY + "-" + clean + "-" + id.toLowerCase(Locale.ROOT).replaceAll("[^0-9a-f]", "");
	}

	/** Every client tick (render thread): open, create or switch to the wanted world. */
	static void tick(final Minecraft minecraft, final boolean hostConnected) {
		if (Boolean.getBoolean("mineheim.noAutoWorld")) {
			return;
		}

		String target = wanted;
		if (minecraft.level != null) {
			titleTicks = 0;
			if (!target.isEmpty() && !target.equals(open) && !switching && minecraft.getSingleplayerServer() != null) {
				switching = true;
				MineHeim.LOG.info("switching Minecraft world {} -> {} (saving it first)", open.isEmpty() ? "?" : open, target);
				minecraft.disconnectWithSavingScreen();
			}

			return;
		}

		// no world open: wait until the last one has finished saving, then open the wanted one
		if (minecraft.getSingleplayerServer() != null) {
			return;
		}

		if (target.isEmpty()) {
			if (!(minecraft.gui.screen() instanceof TitleScreen) || hostConnected || ++titleTicks < NO_HOST_TICKS || !open.isEmpty()) {
				return; // the host says which world once its player is in one
			}

			target = LEGACY;
		}

		if (target.equals(open) && !switching) {
			return; // already asked to open it (it is loading)
		}

		switching = false;
		open = target;
		adoptLegacy(minecraft, target);
		MineHeimClient.openWorld(minecraft, target);
	}

	/** The first host world takes over the shared save of earlier versions (renamed), if no host world exists yet. */
	private static void adoptLegacy(final Minecraft minecraft, final String target) {
		if (target.equals(LEGACY)) {
			return;
		}

		Path base = minecraft.getLevelSource().getBaseDir();
		Path legacy = base.resolve(LEGACY);
		if (!Files.isDirectory(legacy) || Files.exists(base.resolve(target))) {
			return;
		}

		try (Stream<Path> saves = Files.list(base)) {
			if (saves.anyMatch(p -> p.getFileName().toString().startsWith(LEGACY + "-"))) {
				return; // another host world already took it
			}
		} catch (IOException e) {
			return;
		}

		try {
			Files.move(legacy, base.resolve(target));
			MineHeim.LOG.info("the shared Minecraft world becomes {}'s (everything built so far stays)", target);
		} catch (IOException e) {
			MineHeim.LOG.warn("could not take over the shared world for {}: {}", target, e.toString());
		}
	}
}
