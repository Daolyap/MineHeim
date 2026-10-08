package dev.daolyap.mineheim;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.monster.hoglin.Hoglin;
import net.minecraft.world.entity.monster.piglin.AbstractPiglin;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.phys.AABB;

/**
 * The Nether comes to the host's world, never the other way round: a lit Nether portal "infects" the land around it.
 * Nobody travels (PortalMixin): instead the host's ground within a growing radius of the portal turns into a crust of
 * Nether blocks (netherrack, crimson and warped nylium, soul soil, magma, ...) with fire, roots and fungi on top, and
 * Nether mobs (zombified piglins, piglins, hoglins, magma cubes, blazes, wither skeletons, the odd ghast) spawn on it.
 * Break the portal and the infection stops and heals back, outside in, until the land is as it was.
 *
 * <p>The host's ground here is barrier blocks just under its surface (its GroundProbe: the barrier tops out a whole
 * block under the real surface), and the host sends the exact surface too ({"t":"heights"}). The crust goes into the
 * cell the host's surface runs through, just above the barriers, so it shows as a blocky Nether skin poking up to a block out of the host's terrain. Only air is ever replaced,
 * and only blocks still exactly as placed are taken away again: the player's builds are never touched. The crust is not
 * reported to the host as collision (it would turn the host's ground into steps); decorations are (they're not solid,
 * but fire is a light: the host lights its world with it).
 *
 * <p>Inside the host's wards (the host sends them: {"t":"wards"}) nothing grows, and a ward placed on infected land
 * cleans its area. The infections live in the world folder (mineheim/infections.json), so they outlast a restart.
 * Server thread only, except where noted.
 */
public final class NetherInfection {
	/** One infection: a portal and the land it has turned. */
	static final class Infection {
		int id;
		/** Portal blocks that belong to it (the frame's inside); empty = the portal is gone and the land heals. */
		final Set<Long> portal = new HashSet<>();
		double cx, cz;
		int cy;
		/** Current radius in blocks (grows while lit, shrinks while not). */
		double radius;
		/** Cells this infection placed: cell -> the block state id it placed there (crust and decorations). */
		final Map<Long, Integer> placed = new HashMap<>();
		/** Columns that got their crust (cell of the crust block) and columns still waiting for the host's ground. */
		final Map<Long, Long> crusted = new HashMap<>();
		final Set<Long> pending = new HashSet<>();
		/** How far along the offsets (sorted by distance) the crust has gone. */
		int cursor;
		/** Ticks since the last mob spawn try. */
		int spawnTimer;

		boolean lit() {
			return !this.portal.isEmpty();
		}
	}

	private static final List<Infection> infections = new ArrayList<>();
	private static final String MOB_TAG = "mineheim_nether";

	/** Settings from the host ({"t":"nethercfg"}); these are the defaults. Volatile: written by the link thread. */
	static volatile boolean enabled = true;
	static volatile double maxRadius = 48, growPerMinute = 8, healPerMinute = 16;
	static volatile int mobCap = 10, spawnSeconds = 8;

	/** The host's wards, Minecraft x, z, radius per ward (link thread writes, server thread reads). */
	private static volatile float[] wards = new float[0];
	private static volatile boolean wardsChanged;

	/** For the client's ash and spores: x, y, z, radius per infection that is lit or still healing (any thread reads). */
	public static volatile float[] zones = new float[0];

	private static final int MAX_RADIUS_LIMIT = 96;
	/** Column offsets inside MAX_RADIUS_LIMIT, nearest first: {dx, dz} pairs. */
	private static final int[] OFFSETS;
	private static final float[] OFFSET_DIST;

	static {
		List<int[]> list = new ArrayList<>();
		for (int dx = -MAX_RADIUS_LIMIT; dx <= MAX_RADIUS_LIMIT; dx++) {
			for (int dz = -MAX_RADIUS_LIMIT; dz <= MAX_RADIUS_LIMIT; dz++) {
				if (dx * dx + dz * dz <= MAX_RADIUS_LIMIT * MAX_RADIUS_LIMIT) {
					list.add(new int[] {dx, dz});
				}
			}
		}

		list.sort((a, b) -> Integer.compare(a[0] * a[0] + a[1] * a[1], b[0] * b[0] + b[1] * b[1]));
		OFFSETS = new int[list.size() * 2];
		OFFSET_DIST = new float[list.size()];
		for (int i = 0; i < list.size(); i++) {
			OFFSETS[i * 2] = list.get(i)[0];
			OFFSETS[i * 2 + 1] = list.get(i)[1];
			OFFSET_DIST[i] = (float) Math.sqrt(list.get(i)[0] * list.get(i)[0] + list.get(i)[1] * list.get(i)[1]);
		}
	}

	private static int nextId = 1;
	private static int ticks;
	private static boolean dirty;
	private static String lastReport = "";
	/** Ground more than this many blocks above or below the portal is not crusted (a cliff, a mountain top). */
	private static final int SEARCH = 40;
	/** Columns crusted per tick, over all infections. */
	private static final int COLUMNS_PER_TICK = 96;

	private NetherInfection() {
	}

	// ------------------------------------------------------------------ settings and wards from the host (link thread)

	/** {"t":"nethercfg","on":bool,"max":blocks,"grow":blocks a minute,"heal":blocks a minute,"mobs":cap,"every":seconds}. */
	public static void configure(final JsonObject m) {
		enabled = !m.has("on") || m.get("on").getAsBoolean();
		maxRadius = Math.max(4, Math.min(MAX_RADIUS_LIMIT, m.has("max") ? m.get("max").getAsDouble() : maxRadius));
		growPerMinute = Math.max(0, m.has("grow") ? m.get("grow").getAsDouble() : growPerMinute);
		healPerMinute = Math.max(0.5, m.has("heal") ? m.get("heal").getAsDouble() : healPerMinute);
		mobCap = Math.max(0, m.has("mobs") ? m.get("mobs").getAsInt() : mobCap);
		spawnSeconds = Math.max(1, m.has("every") ? m.get("every").getAsInt() : spawnSeconds);
		MineHeim.LOG.info("nether infection: on={} max={} grow={}/min heal={}/min mobs={} every {} s", enabled, maxRadius, growPerMinute, healPerMinute, mobCap, spawnSeconds);
	}

	/** {"t":"wards","w":[x,z,radius,...]} in Minecraft coordinates: the host's active wards near its player. */
	public static void wards(final JsonArray w) {
		float[] out = new float[w.size() - w.size() % 3];
		for (int i = 0; i < out.length; i++) {
			out[i] = w.get(i).getAsFloat();
		}

		if (!Arrays.equals(out, wards)) {
			wards = out;
			wardsChanged = true;
		}
	}

	private static boolean inWard(final int x, final int z) {
		float[] w = wards;
		for (int i = 0; i + 2 < w.length; i += 3) {
			float dx = x + 0.5f - w[i], dz = z + 0.5f - w[i + 1];
			if (dx * dx + dz * dz < w[i + 2] * w[i + 2]) {
				return true;
			}
		}

		return false;
	}

	// ------------------------------------------------------------------ lifecycle

	static void attach(final MinecraftServer s) {
		infections.clear();
		nextId = 1;
		Path file = file(s);
		if (Files.exists(file)) {
			try {
				load(JsonParser.parseString(Files.readString(file, StandardCharsets.UTF_8)).getAsJsonObject());
				MineHeim.LOG.info("nether infection: {} loaded", infections.size());
			} catch (IOException | RuntimeException e) {
				MineHeim.LOG.warn("nether infection: could not read {}: {}", file, e.toString());
			}
		}

		lastReport = "";
		publishZones();
	}

	static void detach(final MinecraftServer s) {
		save(s);
		infections.clear();
		zones = new float[0];
	}

	private static Path file(final MinecraftServer s) {
		return s.getWorldPath(LevelResource.ROOT).resolve("mineheim").resolve("infections.json");
	}

	/** Server thread, from Level.setBlock (every change, whoever made it): portals lit and broken. */
	static void onBlockChanged(final ServerLevel level, final BlockPos pos, final BlockState state) {
		long key = pos.asLong();
		if (state.is(Blocks.NETHER_PORTAL)) {
			if (!enabled) {
				return;
			}

			Infection in = null;
			for (Infection i : infections) {
				if (i.portal.contains(key) || (Math.abs(pos.getX() - i.cx) < 12 && Math.abs(pos.getZ() - i.cz) < 12 && Math.abs(pos.getY() - i.cy) < 24)) {
					in = i;
					break;
				}
			}

			boolean fresh = in == null;
			if (fresh) {
				in = new Infection();
				in.id = nextId++;
				in.radius = 2;
				infections.add(in);
			}

			boolean relit = !fresh && !in.lit();
			in.portal.add(key);
			centre(in);
			dirty = true;
			if (fresh || relit) {
				MineHeim.LOG.info("nether infection {} {} at {} {} {}", in.id, fresh ? "started" : "lit again", (int) in.cx, in.cy, (int) in.cz);
			}
		} else {
			for (Infection i : infections) {
				if (i.portal.remove(key)) {
					dirty = true;
					if (!i.lit()) {
						MineHeim.LOG.info("nether infection {}: portal broken, healing from radius {}", i.id, String.format(Locale.ROOT, "%.1f", i.radius));
					}
				}
			}
		}
	}

	private static void centre(final Infection in) {
		double sx = 0, sz = 0;
		int minY = Integer.MAX_VALUE;
		for (long k : in.portal) {
			BlockPos p = BlockPos.of(k);
			sx += p.getX() + 0.5;
			sz += p.getZ() + 0.5;
			minY = Math.min(minY, p.getY());
		}

		if (!in.portal.isEmpty()) {
			in.cx = sx / in.portal.size();
			in.cz = sz / in.portal.size();
			in.cy = minY - 1; // the frame's bottom row stands on the ground
		}
	}

	/** Is this one of the crust cells (not collision for the host, not reported by a block sync)? Server thread. */
	static boolean isCrust(final BlockPos pos) {
		if (infections.isEmpty()) {
			return false;
		}

		long key = pos.asLong();
		for (Infection i : infections) {
			if (i.placed.containsKey(key)) {
				return true;
			}
		}

		return false;
	}

	/**
	 * The host's ground moved at a column (a crater, a dug or raised spot): crust that no longer sits on the surface
	 * goes, and the column is crusted again on its new ground. Server thread, from WorldBridge.heights.
	 */
	static void onSurfaceChanged(final ServerLevel level, final int x, final int z, final int cell) {
		if (infections.isEmpty()) {
			return;
		}

		long col = column(x, z);
		for (Infection i : infections) {
			Long crust = i.crusted.get(col);
			if (crust != null && i.placed.containsKey(crust) && BlockPos.of(crust).getY() != cell) {
				BlockPos at = BlockPos.of(crust);
				unplace(level, i, at.above());
				unplace(level, i, at);
				i.crusted.remove(col);
				i.pending.add(col);
				dirty = true;
			}
		}
	}

	// ------------------------------------------------------------------ every server tick

	static void tick(final MinecraftServer s) {
		ticks++;
		if (ticks % 10 == 0 && MineHeim.active) {
			burn(s);
		}

		if (infections.isEmpty() || WorldBridge.server() == null) {
			if (ticks % 20 == 0) {
				report();
			}

			return;
		}

		ServerLevel level = s.overworld();
		boolean placing = WorldBridge.placingGround;
		int budget = COLUMNS_PER_TICK;
		if (wardsChanged) {
			wardsChanged = false;
			for (Infection i : infections) {
				cleanseWards(level, i);
			}
		}

		for (Iterator<Infection> it = infections.iterator(); it.hasNext();) {
			Infection i = it.next();
			if (i.lit() && enabled) {
				i.radius = Math.min(maxRadius, i.radius + growPerMinute / 1200.0);
				budget -= grow(level, i, budget);
				spawnMobs(level, i);
			} else {
				i.radius -= healPerMinute / 1200.0;
				if (ticks % 10 == 0) {
					heal(level, i);
				}

				if (i.radius <= 0 && i.placed.isEmpty()) {
					MineHeim.LOG.info("nether infection {}: healed", i.id);
					it.remove();
					dirty = true;
				}
			}
		}

		WorldBridge.placingGround = placing;
		if (ticks % 20 == 0) {
			publishZones();
			report();
		}

		if (dirty && ticks % 600 == 0) {
			save(s);
		}
	}

	/** Crust the next columns inside the radius; returns how many columns it looked at. */
	private static int grow(final ServerLevel level, final Infection i, final int budget) {
		int used = 0;
		int ox = (int) Math.floor(i.cx), oz = (int) Math.floor(i.cz);
		while (used < budget && i.cursor < OFFSET_DIST.length && OFFSET_DIST[i.cursor] <= i.radius) {
			int x = ox + OFFSETS[i.cursor * 2], z = oz + OFFSETS[i.cursor * 2 + 1];
			i.cursor++;
			used++;
			long col = column(x, z);
			if (!i.crusted.containsKey(col) && !crustColumn(level, i, x, z)) {
				i.pending.add(col);
			}
		}

		// once a second, a share of the columns that waited (no ground yet, a ward, the ragged edge)
		if (ticks % 20 == i.id % 20 && !i.pending.isEmpty() && used < budget) {
			for (Iterator<Long> it = i.pending.iterator(); it.hasNext() && used < budget * 4;) {
				long col = it.next();
				used++;
				int x = (int) (col >> 32), z = (int) col;
				if (distance(i, x, z) > i.radius + 1) {
					continue;
				}

				if (i.crusted.containsKey(col) || crustColumn(level, i, x, z)) {
					it.remove();
				}
			}
		}

		return Math.min(used, budget);
	}

	/** Lay the crust (and maybe a decoration) on one column; false if it has to wait (no ground, a ward, beyond the ragged edge). */
	private static boolean crustColumn(final ServerLevel level, final Infection i, final int x, final int z) {
		double d = distance(i, x, z);
		if (d + edge(x, z) * 3.0 > i.radius || inWard(x, z)) {
			return false;
		}

		// the host's exact surface there; the crust goes in the cell it runs through (just over the barrier ground)
		double surface = WorldBridge.surface(x, z);
		if (Double.isNaN(surface) || Math.abs(surface - i.cy) > SEARCH || !level.isLoaded(BlockPos.containing(x, surface, z))) {
			return false;
		}

		// a tree's trunk or a post reads as ground: the crust only goes where the ground is about as high as around it
		double around = 0;
		int n = 0;
		for (int k = 0; k < 4; k++) {
			double h = WorldBridge.surface(x + (k == 0 ? 2 : k == 1 ? -2 : 0), z + (k == 2 ? 2 : k == 3 ? -2 : 0));
			if (!Double.isNaN(h)) {
				around += h;
				n++;
			}
		}

		if (n > 0 && surface - around / n > 1.5) {
			i.crusted.put(column(x, z), BlockPos.containing(x, surface, z).asLong()); // never crusted: done
			return true;
		}

		int top = (int) Math.floor(surface) - 1;
		BlockPos crust = new BlockPos(x, top + 1, z);
		if (!level.getBlockState(crust).isAir()) {
			// something stands there (the player's block, the portal's frame): leave it, the column counts as done
			i.crusted.put(column(x, z), crust.asLong());
			return true;
		}

		long h = hash(x, z, 1);
		double near = d < 3.0 ? 0.0 : 1.0; // the ground at the portal stays plain netherrack
		BlockState crustState = crust(x, z, h, near);
		place(level, i, crust, crustState, true);
		BlockState deco = near > 0 ? decoration(crustState, h) : null;
		if (deco != null && level.getBlockState(crust.above()).isAir()) {
			place(level, i, crust.above(), deco, false);
		}

		i.crusted.put(column(x, z), crust.asLong());
		dirty = true;
		return true;
	}

	private static void place(final ServerLevel level, final Infection i, final BlockPos pos, final BlockState state, final boolean crust) {
		// the crust is not news to the host (not its collision); decorations are (fire is a light for it)
		WorldBridge.placingGround = crust;
		level.setBlock(pos, state, Block.UPDATE_ALL);
		WorldBridge.placingGround = false;
		i.placed.put(pos.asLong(), Block.getId(state));
	}

	/** Take one placed cell away again, if it is still exactly what was placed (the player may have mined or built there). */
	private static void unplace(final ServerLevel level, final Infection i, final BlockPos pos) {
		Integer id = i.placed.remove(pos.asLong());
		if (id == null) {
			return;
		}

		BlockState now = level.getBlockState(pos);
		if (Block.getId(now) == id || now.is(Blocks.FIRE) || now.is(Blocks.SOUL_FIRE)) {
			WorldBridge.placingGround = true;
			level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_ALL);
			WorldBridge.placingGround = false;
		}
	}

	/** Shrink: every column now outside the (ragged) radius loses its crust, nearest the edge first by construction. */
	private static void heal(final ServerLevel level, final Infection i) {
		int done = 0;
		for (Iterator<Map.Entry<Long, Long>> it = i.crusted.entrySet().iterator(); it.hasNext() && done < 400;) {
			Map.Entry<Long, Long> e = it.next();
			int x = (int) (e.getKey() >> 32), z = (int) (long) e.getKey();
			if (distance(i, x, z) + edge(x, z) * 3.0 > Math.max(0, i.radius)) {
				BlockPos crust = BlockPos.of(e.getValue());
				if (!level.isLoaded(crust)) {
					continue; // healed when the player is back near it
				}

				unplace(level, i, crust.above());
				unplace(level, i, crust);
				it.remove();
				done++;
				dirty = true;
			}
		}

		i.pending.removeIf(col -> distance(i, (int) (col >> 32), (int) (long) col) > Math.max(0, i.radius) + 1);
		// the crust starts over from the new edge if the portal is lit again
		while (i.cursor > 0 && OFFSET_DIST[i.cursor - 1] > Math.max(0, i.radius)) {
			i.cursor--;
		}

		if (i.radius <= 0 && !i.placed.isEmpty() && i.crusted.isEmpty()) {
			// stray cells (a column whose crust was mined while its decoration stayed): clear what is loaded
			for (Iterator<Long> it = new ArrayList<>(i.placed.keySet()).iterator(); it.hasNext();) {
				BlockPos pos = BlockPos.of(it.next());
				if (level.isLoaded(pos)) {
					unplace(level, i, pos);
				}
			}
		}
	}

	/** A ward placed on infected land cleans its area. */
	private static void cleanseWards(final ServerLevel level, final Infection i) {
		for (Iterator<Map.Entry<Long, Long>> it = i.crusted.entrySet().iterator(); it.hasNext();) {
			Map.Entry<Long, Long> e = it.next();
			int x = (int) (e.getKey() >> 32), z = (int) (long) e.getKey();
			if (inWard(x, z)) {
				BlockPos crust = BlockPos.of(e.getValue());
				unplace(level, i, crust.above());
				unplace(level, i, crust);
				it.remove();
				i.pending.add(e.getKey());
				dirty = true;
			}
		}
	}

	/**
	 * The host moves the player with Minecraft's block contact off (PlayerMixin: noPhysics), so fire and lava never
	 * touch it by themselves: twice a second, fire, soul fire or lava where it stands, or a magma block under it, burn
	 * the host's player outside Creative ({"t":"hurt",...,"fire":true}, Minecraft points like Minecraft's own: fire 1,
	 * lava 4, magma 1 a second, here per half second halved).
	 */
	private static void burn(final MinecraftServer s) {
		List<ServerPlayer> players = s.getPlayerList().getPlayers();
		if (players.isEmpty() || players.get(0).isCreative() || players.get(0).isSpectator()) {
			return;
		}

		ServerPlayer player = players.get(0);
		ServerLevel level = s.overworld();
		BlockPos feet = BlockPos.containing(player.getX(), player.getY() + 0.1, player.getZ());
		BlockState at = level.getBlockState(feet);
		BlockState below = level.getBlockState(feet.below());
		float amount = at.is(Blocks.LAVA) ? 2f : at.is(Blocks.FIRE) || at.is(Blocks.SOUL_FIRE) ? 0.5f
			: below.is(Blocks.MAGMA_BLOCK) && !player.isShiftKeyDown() ? 0.5f : 0f;
		if (amount > 0f) {
			String what = at.is(Blocks.LAVA) ? "lava" : at.is(Blocks.FIRE) || at.is(Blocks.SOUL_FIRE) ? "fire" : "magma";
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"hurt\",\"amount\":%.2f,\"src\":\"%s\",\"fire\":true,\"pos\":[%.3f,%.3f,%.3f]}",
				amount, what, player.getX(), player.getY(), player.getZ()));
		}
	}

	// ------------------------------------------------------------------ what the crust is made of

	/** The Nether's floors in patches: mostly netherrack wastes, with crimson and warped forest floor and soul soil valleys. */
	private static BlockState crust(final int x, final int z, final long h, final double near) {
		if (near == 0.0) {
			return Blocks.NETHERRACK.defaultBlockState();
		}

		double biome = noise(x, z, 9.0, 7);
		int roll = (int) (h % 100);
		if (biome > 0.78) {
			return roll < 55 ? Blocks.SOUL_SOIL.defaultBlockState() : roll < 90 ? Blocks.SOUL_SAND.defaultBlockState() : Blocks.BASALT.defaultBlockState();
		}

		if (biome > 0.66) {
			return roll < 85 ? Blocks.WARPED_NYLIUM.defaultBlockState() : Blocks.NETHERRACK.defaultBlockState();
		}

		if (biome > 0.50) {
			return roll < 85 ? Blocks.CRIMSON_NYLIUM.defaultBlockState() : Blocks.NETHERRACK.defaultBlockState();
		}

		if (roll < 7) {
			return Blocks.MAGMA_BLOCK.defaultBlockState();
		}

		if (roll < 10) {
			return Blocks.NETHER_GOLD_ORE.defaultBlockState();
		}

		if (roll < 13) {
			return Blocks.NETHER_QUARTZ_ORE.defaultBlockState();
		}

		if (roll < 18) {
			return Blocks.BLACKSTONE.defaultBlockState();
		}

		return Blocks.NETHERRACK.defaultBlockState();
	}

	/** What grows or burns on a crust block, or null. */
	private static BlockState decoration(final BlockState crust, final long h) {
		int roll = (int) ((h >> 8) % 100);
		if (crust.is(Blocks.NETHERRACK)) {
			return roll < 5 ? Blocks.FIRE.defaultBlockState() : null;
		}

		if (crust.is(Blocks.CRIMSON_NYLIUM)) {
			return roll < 22 ? Blocks.CRIMSON_ROOTS.defaultBlockState() : roll < 28 ? Blocks.CRIMSON_FUNGUS.defaultBlockState() : null;
		}

		if (crust.is(Blocks.WARPED_NYLIUM)) {
			return roll < 15 ? Blocks.WARPED_ROOTS.defaultBlockState() : roll < 25 ? Blocks.NETHER_SPROUTS.defaultBlockState()
				: roll < 30 ? Blocks.WARPED_FUNGUS.defaultBlockState() : null;
		}

		if (crust.is(Blocks.SOUL_SOIL)) {
			return roll < 6 ? Blocks.SOUL_FIRE.defaultBlockState() : null;
		}

		return null;
	}

	/** 0..1, how ragged the edge is at this column (so the infection's edge isn't a perfect circle). */
	private static double edge(final int x, final int z) {
		return noise(x, z, 5.0, 3);
	}

	/** Smooth value noise in 0..1 with cells `scale` blocks wide. */
	private static double noise(final int x, final int z, final double scale, final int salt) {
		double fx = x / scale, fz = z / scale;
		int x0 = (int) Math.floor(fx), z0 = (int) Math.floor(fz);
		double tx = smooth(fx - x0), tz = smooth(fz - z0);
		double a = unit(x0, z0, salt), b = unit(x0 + 1, z0, salt), c = unit(x0, z0 + 1, salt), d = unit(x0 + 1, z0 + 1, salt);
		return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
	}

	private static double smooth(final double t) {
		return t * t * (3 - 2 * t);
	}

	private static double unit(final int x, final int z, final int salt) {
		return (hash(x, z, salt) & 0xFFFF) / 65535.0;
	}

	private static long hash(final int x, final int z, final int salt) {
		long h = x * 0x9E3779B97F4A7C15L ^ z * 0xC2B2AE3D27D4EB4FL ^ salt * 0x165667B19E3779F9L;
		h ^= h >>> 33;
		h *= 0xFF51AFD7ED558CCDL;
		h ^= h >>> 33;
		return h & Long.MAX_VALUE;
	}

	private static long column(final int x, final int z) {
		return ((long) x << 32) | (z & 0xFFFFFFFFL);
	}

	private static double distance(final Infection i, final int x, final int z) {
		double dx = x + 0.5 - i.cx, dz = z + 0.5 - i.cz;
		return Math.sqrt(dx * dx + dz * dz);
	}

	// ------------------------------------------------------------------ the Nether's mobs

	private record Spawn(EntityType<? extends Mob> type, int weight, int lift, double minRadius) {
	}

	private static final Spawn[] SPAWNS = {
		new Spawn(EntityTypes.ZOMBIFIED_PIGLIN, 30, 0, 0),
		new Spawn(EntityTypes.PIGLIN, 16, 0, 0),
		new Spawn(EntityTypes.MAGMA_CUBE, 16, 0, 0),
		new Spawn(EntityTypes.HOGLIN, 10, 0, 10),
		new Spawn(EntityTypes.BLAZE, 10, 1, 12),
		new Spawn(EntityTypes.WITHER_SKELETON, 8, 0, 16),
		new Spawn(EntityTypes.GHAST, 4, 12, 24),
	};

	private static void spawnMobs(final ServerLevel level, final Infection i) {
		if (mobCap <= 0 || ++i.spawnTimer < spawnSeconds * 20 || i.radius < 6 || i.crusted.isEmpty()) {
			return;
		}

		i.spawnTimer = 0;
		List<ServerPlayer> players = level.getServer().getPlayerList().getPlayers();
		ServerPlayer player = players.isEmpty() ? null : players.get(0);
		if (player == null || player.distanceToSqr(i.cx, player.getY(), i.cz) > (i.radius + 64) * (i.radius + 64)) {
			return; // nobody near: nothing spawns (and Minecraft would despawn it anyway)
		}

		AABB area = new AABB(i.cx - i.radius - 24, i.cy - 48, i.cz - i.radius - 24, i.cx + i.radius + 24, i.cy + 64, i.cz + i.radius + 24);
		int count = level.getEntitiesOfClass(Mob.class, area, m -> m.entityTags().contains(MOB_TAG)).size();
		int cap = Math.min(mobCap, 2 + (int) (i.radius / 6));
		if (count >= cap) {
			return;
		}

		RandomSource random = level.getRandom();
		Long[] columns = i.crusted.values().toArray(new Long[0]);
		for (int attempt = 0; attempt < 8; attempt++) {
			BlockPos crust = BlockPos.of(columns[random.nextInt(columns.length)]);
			double dp = Math.sqrt(player.distanceToSqr(crust.getX() + 0.5, player.getY(), crust.getZ() + 0.5));
			if (dp < 10 || dp > 56 || distance(i, crust.getX(), crust.getZ()) < 4 || !level.getBlockState(crust).isSolid()) {
				continue;
			}

			Spawn pick = pick(random, i.radius);
			BlockPos at = crust.above(1 + pick.lift());
			if (!level.getBlockState(at).isAir() && !level.getBlockState(at).canBeReplaced()) {
				continue;
			}

			Mob mob = pick.type().spawn(level, at, EntitySpawnReason.EVENT);
			if (mob == null) {
				return; // e.g. Peaceful
			}

			mob.addTag(MOB_TAG);
			// in the Overworld piglins and hoglins turn into zombies after 15 s; on this Nether ground they don't
			if (mob instanceof AbstractPiglin piglin) {
				piglin.setImmuneToZombification(true);
			} else if (mob instanceof Hoglin hoglin) {
				hoglin.setImmuneToZombification(true);
			}

			MineHeim.LOG.info("nether infection {}: {} spawned at {} {} {}", i.id, EntityType.getKey(pick.type()).getPath(), at.getX(), at.getY(), at.getZ());
			return;
		}
	}

	private static Spawn pick(final RandomSource random, final double radius) {
		int total = 0;
		for (Spawn s : SPAWNS) {
			total += radius >= s.minRadius() ? s.weight() : 0;
		}

		int r = random.nextInt(Math.max(1, total));
		for (Spawn s : SPAWNS) {
			if (radius >= s.minRadius()) {
				r -= s.weight();
				if (r < 0) {
					return s;
				}
			}
		}

		return SPAWNS[0];
	}

	/** Every mob the infections spawned (test hook and "nether clear"). */
	static int countMobs(final ServerLevel level) {
		int n = 0;
		for (Entity e : level.getAllEntities()) {
			n += e.entityTags().contains(MOB_TAG) ? 1 : 0;
		}

		return n;
	}

	// ------------------------------------------------------------------ host side: reports, commands, the file

	/** {"t":"infect","l":[[id,x,y,z,radius,lit,crusted columns,placed cells,waiting columns],...],"mobs":n}, once a second when it changed. */
	private static void report() {
		if (!MineHeim.active) {
			return;
		}

		StringBuilder b = new StringBuilder("{\"t\":\"infect\",\"l\":[");
		for (int k = 0; k < infections.size(); k++) {
			Infection i = infections.get(k);
			b.append(k == 0 ? "" : ",").append(String.format(Locale.ROOT, "[%d,%.2f,%d,%.2f,%.2f,%d,%d,%d,%d]", i.id, i.cx, i.cy, i.cz, Math.max(0, i.radius),
				i.lit() ? 1 : 0, i.crusted.size(), i.placed.size(), i.pending.size()));
		}

		MinecraftServer s = WorldBridge.server();
		String message = b.append("],\"mobs\":").append(s == null ? 0 : countMobs(s.overworld())).append('}').toString();
		if (!message.equals(lastReport)) {
			lastReport = message;
			MineHeim.events.accept(message);
		}
	}

	private static void publishZones() {
		float[] z = new float[infections.size() * 4];
		for (int k = 0; k < infections.size(); k++) {
			Infection i = infections.get(k);
			z[k * 4] = (float) i.cx;
			z[k * 4 + 1] = i.cy;
			z[k * 4 + 2] = (float) i.cz;
			z[k * 4 + 3] = (float) Math.max(0, i.radius);
		}

		zones = z;
	}

	/** {"t":"nether","a":"clear|heal|grow|portal|unportal"}: test hooks and the host's "make it go away". Link thread. */
	public static void command(final String action) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			switch (action) {
				case "clear" -> {
					// everything back at once: crust, decorations, the infections' mobs (the portals stay, unlit infections can't restart without a new flame)
					for (Infection i : infections) {
						for (Long cell : new ArrayList<>(i.placed.keySet())) {
							unplace(level, i, BlockPos.of(cell));
						}
					}

					infections.clear();
					for (Entity e : level.getAllEntities()) {
						if (e.entityTags().contains(MOB_TAG)) {
							e.discard();
						}
					}

					dirty = true;
					save(s);
					MineHeim.LOG.info("nether infection: cleared");
				}
				case "heal" -> {
					for (Infection i : infections) {
						i.portal.clear();
					}

					dirty = true;
				}
				case "portal" -> {
					// a lit portal 6 blocks east of the player, frame and fire in one go (tests; the player builds his own)
					List<ServerPlayer> players = s.getPlayerList().getPlayers();
					if (!players.isEmpty()) {
						BlockPos base = players.get(0).blockPosition().east(6);
						for (int dy = 0; dy <= 4; dy++) {
							for (int dz = -1; dz <= 2; dz++) {
								boolean inside = dy >= 1 && dy <= 3 && dz >= 0 && dz <= 1;
								level.setBlock(base.offset(0, dy, dz), inside ? Blocks.AIR.defaultBlockState() : Blocks.OBSIDIAN.defaultBlockState(), Block.UPDATE_ALL);
							}
						}

						level.setBlock(base.above(), Blocks.FIRE.defaultBlockState(), Block.UPDATE_ALL);
						MineHeim.LOG.info("nether infection: test portal at {} (lit: {})", base, level.getBlockState(base.above()).is(Blocks.NETHER_PORTAL));
					}
				}
				case "unportal" -> {
					// break every infection's portal: its frame's blocks go (tests)
					for (Infection i : infections) {
						for (long k : new ArrayList<>(i.portal)) {
							BlockPos p = BlockPos.of(k);
							for (BlockPos q : BlockPos.betweenClosed(p.offset(-1, -1, -1), p.offset(1, 1, 1))) {
								BlockState st = level.getBlockState(q);
								if (st.is(Blocks.OBSIDIAN) || st.is(Blocks.NETHER_PORTAL)) {
									level.setBlock(q, Blocks.AIR.defaultBlockState(), Block.UPDATE_ALL);
								}
							}
						}
					}
				}
				case "grow" -> {
					for (Infection i : infections) {
						i.radius = maxRadius;
					}
				}
				default -> {
				}
			}

			lastReport = "";
			report();
		});
	}

	private static void save(final MinecraftServer s) {
		if (!dirty) {
			return;
		}

		JsonObject root = new JsonObject();
		root.addProperty("next", nextId);
		JsonArray list = new JsonArray();
		for (Infection i : infections) {
			JsonObject o = new JsonObject();
			o.addProperty("id", i.id);
			o.addProperty("radius", i.radius);
			o.addProperty("cx", i.cx);
			o.addProperty("cy", i.cy);
			o.addProperty("cz", i.cz);
			o.add("portal", longs(i.portal));
			JsonArray placed = new JsonArray();
			for (Map.Entry<Long, Integer> e : i.placed.entrySet()) {
				placed.add(e.getKey());
				placed.add(e.getValue());
			}

			o.add("placed", placed);
			JsonArray crusted = new JsonArray();
			for (Map.Entry<Long, Long> e : i.crusted.entrySet()) {
				crusted.add(e.getKey());
				crusted.add(e.getValue());
			}

			o.add("crusted", crusted);
			o.add("pending", longs(i.pending));
			list.add(o);
		}

		root.add("infections", list);
		Path file = file(s);
		try {
			Files.createDirectories(file.getParent());
			Gson gson = new GsonBuilder().create();
			Files.writeString(file, gson.toJson(root), StandardCharsets.UTF_8);
			dirty = false;
		} catch (IOException e) {
			MineHeim.LOG.warn("nether infection: could not write {}: {}", file, e.toString());
		}
	}

	private static void load(final JsonObject root) {
		nextId = root.has("next") ? root.get("next").getAsInt() : 1;
		for (var el : root.getAsJsonArray("infections")) {
			JsonObject o = el.getAsJsonObject();
			Infection i = new Infection();
			i.id = o.get("id").getAsInt();
			i.radius = o.get("radius").getAsDouble();
			i.cx = o.get("cx").getAsDouble();
			i.cy = o.get("cy").getAsInt();
			i.cz = o.get("cz").getAsDouble();
			o.getAsJsonArray("portal").forEach(v -> i.portal.add(v.getAsLong()));
			JsonArray placed = o.getAsJsonArray("placed");
			for (int k = 0; k + 1 < placed.size(); k += 2) {
				i.placed.put(placed.get(k).getAsLong(), placed.get(k + 1).getAsInt());
			}

			JsonArray crusted = o.getAsJsonArray("crusted");
			for (int k = 0; k + 1 < crusted.size(); k += 2) {
				i.crusted.put(crusted.get(k).getAsLong(), crusted.get(k + 1).getAsLong());
			}

			o.getAsJsonArray("pending").forEach(v -> i.pending.add(v.getAsLong()));
			// the crust picks up where it was: every offset already inside the radius was visited
			while (i.cursor < OFFSET_DIST.length && OFFSET_DIST[i.cursor] <= i.radius) {
				i.cursor++;
			}

			infections.add(i);
			nextId = Math.max(nextId, i.id + 1);
		}
	}

	private static JsonArray longs(final Set<Long> values) {
		JsonArray a = new JsonArray();
		values.forEach(a::add);
		return a;
	}
}
