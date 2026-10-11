package dev.daolyap.mineheim;

import dev.daolyap.mineheim.mixin.AbstractArrowAccessor;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import it.unimi.dsi.fastutil.objects.Object2IntMap;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.FireworkRocketEntity;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.enchantment.Enchantment;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.Vec3;

/** Server-side half: the host's collision as invisible barrier blocks, commands, and events back to the host. */
public final class WorldBridge {
	private static volatile MinecraftServer server;
	/** Barriers we placed (so a reset only removes ours, never the player's builds). */
	private static final Set<BlockPos> barriers = ConcurrentHashMap.newKeySet();
	/** Block changes this tick (server thread only): true = now solid, false = gone. Sent at the end of the tick. */
	private static final Map<BlockPos, Boolean> changes = new LinkedHashMap<>();
	/** While placing or removing the host's own ground or lights: those changes aren't news to the host (server thread only). */
	static boolean placingGround;
	/** The host's exact surface per column, Minecraft y in cm ({"t":"heights"}): the Nether crust is laid on it. */
	private static final Map<Long, Integer> surfaces = new ConcurrentHashMap<>();

	private WorldBridge() {
	}

	static void attach(final MinecraftServer s) {
		server = s;
		NetherInfection.attach(s);
		// the host (re)sends its ground when a world starts: barriers sent before this point had nowhere to go
		MineHeim.events.accept("{\"t\":\"ready\"}");
	}

	static void detach() {
		MinecraftServer s = server;
		if (s != null) {
			NetherInfection.detach(s);
		}

		server = null;
		surfaces.clear();
		barriers.clear();
		HostLights.detach();
		Creatures.detach();
	}

	public static boolean ready() {
		return server != null;
	}

	static MinecraftServer server() {
		return server;
	}

	/** Columns of solid ground from the host: {x, z, yBottom, yTop, ...} in block coordinates (inclusive). Only air is replaced. */
	public static void solid(final int[] columns) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			BlockState barrier = Blocks.BARRIER.defaultBlockState();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			placingGround = true;
			for (int i = 0; i + 3 < columns.length; i += 4) {
				for (int y = columns[i + 2]; y <= columns[i + 3]; y++) {
					pos.set(columns[i], y, columns[i + 1]);
					if (level.isInWorldBounds(pos) && level.getBlockState(pos).isAir()) {
						level.setBlock(pos, barrier, Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
						barriers.add(pos.immutable());
					}
				}
			}

			placingGround = false;
		});
	}

	/**
	 * Take barrier columns away again (the host's ground changed there): {x, z, yBottom, yTop, ...}. Only barrier blocks are
	 * removed, never anything the player built. Barriers left in the world by an earlier session count as ours.
	 */
	public static void unsolid(final int[] columns) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			placingGround = true;
			for (int i = 0; i + 3 < columns.length; i += 4) {
				for (int y = columns[i + 2]; y <= columns[i + 3]; y++) {
					pos.set(columns[i], y, columns[i + 1]);
					if (level.isInWorldBounds(pos) && level.getBlockState(pos).is(Blocks.BARRIER)) {
						level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
					}

					barriers.remove(pos);
				}
			}

			placingGround = false;
		});
	}

	/** {"t":"heights","c":[x,z,cm,...]}: the host's exact surface per column (link thread). */
	public static void heights(final int[] c) {
		MinecraftServer s = server;
		for (int i = 0; i + 2 < c.length; i += 3) {
			Integer old = surfaces.put(column(c[i], c[i + 1]), c[i + 2]);
			if (s != null && old != null && Math.floorDiv(old, 100) != Math.floorDiv(c[i + 2], 100)) {
				int x = c[i], z = c[i + 1], cell = Math.floorDiv(c[i + 2], 100);
				s.execute(() -> NetherInfection.onSurfaceChanged(s.overworld(), x, z, cell));
			}
		}
	}

	/** The host's surface at a column (Minecraft y), or NaN if the host hasn't sent it. */
	static double surface(final int x, final int z) {
		Integer cm = surfaces.get(column(x, z));
		return cm == null ? Double.NaN : cm / 100.0;
	}

	private static long column(final int x, final int z) {
		return ((long) x << 32) | (z & 0xFFFFFFFFL);
	}

	/** For the host's ground checks: for each {x, y, z} reply 1 if the cell holds a barrier block, else 0 ({"t":"probe","r":[...]}). */
	public static void probe(final int[] cells) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			StringBuilder reply = new StringBuilder("{\"t\":\"probe\",\"r\":[");
			for (int i = 0; i + 2 < cells.length; i += 3) {
				pos.set(cells[i], cells[i + 1], cells[i + 2]);
				reply.append(i == 0 ? "" : ",").append(level.isInWorldBounds(pos) && level.getBlockState(pos).is(Blocks.BARRIER) ? 1 : 0);
			}

			MineHeim.events.accept(reply.append("]}").toString());
		});
	}

	/** Remove every barrier we placed (e.g. when the host teleports somewhere else). */
	public static void clearSolid() {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			placingGround = true;
			for (BlockPos pos : barriers) {
				if (level.getBlockState(pos).is(Blocks.BARRIER)) {
					level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
				}
			}

			placingGround = false;
			barriers.clear();
		});
	}

	/** Run a command as the server (op). Results go to the log, not to chat (send_command_feedback is off). */
	public static void command(final String command) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			MineHeim.LOG.info("command: {}", command);
			s.getCommands().performPrefixedCommand(s.createCommandSourceStack(), command);
		});
	}

	/** Whether a block should become a collider in the host: any block with a collision shape, except our own barriers. */
	private static boolean solidForHost(final ServerLevel level, final BlockPos pos, final BlockState state) {
		return !state.isAir() && !state.is(Blocks.BARRIER) && !state.getCollisionShape(level, pos).isEmpty() && !NetherInfection.isCrust(pos);
	}

	/** Arrows the host already hit something with (they stay where they hit and aren't reported again). */
	private static final String HIT_TAG = "mineheim_hit";

	/** Every server tick: block changes, and projectiles in flight for the host to trace through its own world. */
	static void tick(final MinecraftServer s) {
		flush(s);
		HostLights.flush();
		NetherInfection.tick(s);
		if (MineHeim.active) {
			reportProjectiles(s.overworld());
			HostItems.tick(s.overworld());
		}
	}

	/**
	 * Steve's arrows (bow, crossbow) and crossbow fireworks in flight: {"t":"proj","p":[[id,kind,x,y,z],...]}. Mobs'
	 * arrows aren't traced by the host: they hit its people's proxies here.
	 */
	private static void reportProjectiles(final ServerLevel level) {
		StringBuilder b = null;
		for (Entity e : level.getAllEntities()) {
			String kind = null;
			if (!(e instanceof Projectile projectile) || !(projectile.getOwner() instanceof Player)) {
				continue;
			}

			if (e instanceof AbstractArrow arrow && !arrow.entityTags().contains(HIT_TAG) && arrow.getDeltaMovement().lengthSqr() > 1.0E-4) {
				kind = "arrow";
			} else if (e instanceof FireworkRocketEntity rocket && rocket.isShotAtAngle()) {
				kind = "firework"; // not the ones boosting an elytra flight
			}

			if (kind != null) {
				// what the arrow would hit for here: ceil(speed x base damage), as Minecraft reckons it (a rocket: 0, it bursts);
				// and the bow or crossbow that shot it, with its enchantments, and whether it burns: the host shoots it as that weapon
				double damage = e instanceof AbstractArrow arrow ? Math.ceil(Math.min(arrow.getDeltaMovement().length() * ((AbstractArrowAccessor) arrow).mineheim$getBaseDamage(), 2.147483647E9)) : 0.0;
				ItemStack weapon = e instanceof AbstractArrow arrow && arrow.getWeaponItem() != null ? arrow.getWeaponItem() : ItemStack.EMPTY;
				b = b == null ? new StringBuilder("{\"t\":\"proj\",\"p\":[") : b.append(',');
				b.append(String.format(Locale.ROOT, "[%d,\"%s\",%.3f,%.3f,%.3f,%.1f,\"%s\",\"%s\",%d]", e.getId(), kind, e.getX(), e.getY(), e.getZ(), damage,
					weapon.isEmpty() ? "" : BuiltInRegistries.ITEM.getKey(weapon.getItem()).getPath(), enchantments(weapon), e.isOnFire() ? 1 : 0));
			}
		}

		if (b != null) {
			MineHeim.events.accept(b.append("]}").toString());
		}
	}

	/** An item's enchantments for the host, "sharpness:5,fire_aspect:2" ("" for none): its Valheim weapon gets them as upgrades and elements. */
	public static String enchantments(final ItemStack stack) {
		if (stack.isEmpty()) {
			return "";
		}

		StringBuilder b = new StringBuilder();
		for (Object2IntMap.Entry<Holder<Enchantment>> entry : stack.getEnchantments().entrySet()) {
			String name = entry.getKey().getRegisteredName();
			int colon = name.indexOf(':');
			b.append(b.length() > 0 ? "," : "").append(colon >= 0 ? name.substring(colon + 1) : name).append(':').append(entry.getIntValue());
		}

		return b.toString();
	}

	/**
	 * The host traced a projectile into something of its own: a firework bursts there; an arrow goes into a person
	 * or car (gone) or sticks where it hit a wall.
	 */
	public static void projectileHit(final int id, final double x, final double y, final double z, final boolean stick) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			Entity e = level.getEntity(id);
			if (e instanceof FireworkRocketEntity) {
				e.setPos(x, y, z);
				level.broadcastEntityEvent(e, (byte) 17);
				e.discard();
			} else if (e instanceof AbstractArrow) {
				if (stick) {
					e.setPos(x, y, z);
					e.setDeltaMovement(Vec3.ZERO);
					e.setNoGravity(true);
					e.addTag(HIT_TAG);
				} else {
					e.discard();
				}
			}
		});
	}

	/** Server thread, from Level.setBlock: remember the change; flushed once per tick. */
	public static void onBlockChanged(final ServerLevel level, final BlockPos pos, final BlockState state) {
		if (level != level.getServer().overworld()) {
			return;
		}

		NetherInfection.onBlockChanged(level, pos, state); // portals are watched with or without a host
		if (!MineHeim.active) {
			return;
		}

		if (!placingGround) {
			changes.put(pos.immutable(), solidForHost(level, pos, state));
			HostLights.onBlockChanged(pos, state);
		}
	}

	/** End of each server tick: {"t":"blocks","set":[x,y,z,...],"clear":[x,y,z,...]}. */
	static void flush(final MinecraftServer s) {
		if (changes.isEmpty()) {
			return;
		}

		StringBuilder set = new StringBuilder();
		StringBuilder clear = new StringBuilder();
		for (Map.Entry<BlockPos, Boolean> e : changes.entrySet()) {
			StringBuilder b = e.getValue() ? set : clear;
			BlockPos p = e.getKey();
			b.append(b.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ());
		}

		changes.clear();
		MineHeim.events.accept("{\"t\":\"blocks\",\"set\":[" + set + "],\"clear\":[" + clear + "]}");
	}

	/** Every solid block within `radius` of the player, as one "blocks" message (the host's props start from this). */
	public static void sync(final int radius) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			ServerPlayer player = s.getPlayerList().getPlayers().isEmpty() ? null : s.getPlayerList().getPlayers().get(0);
			if (player == null) {
				return;
			}

			BlockPos c = player.blockPosition();
			BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
			int found = 0;
			for (int x = -radius; x <= radius && found < 3000; x++) {
				for (int z = -radius; z <= radius && found < 3000; z++) {
					for (int y = -24; y <= 40; y++) {
						p.set(c.getX() + x, c.getY() + y, c.getZ() + z);
						if (!level.isInWorldBounds(p) || !level.isLoaded(p)) {
							continue;
						}

						BlockState state = level.getBlockState(p);
						if (solidForHost(level, p, state)) {
							changes.put(p.immutable(), true);
							found++;
						}

						HostLights.found(p, state);
					}
				}
			}

			flush(s);
			HostLights.flush();
		});
	}

	/** `source`: what exploded or was blown up, e.g. "tnt", "creeper", "fireball" (a ghast's). */
	public static void onExplosion(final Vec3 center, final float radius, final String source) {
		if (MineHeim.active) {
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"explosion\",\"pos\":[%.3f,%.3f,%.3f],\"r\":%.2f,\"src\":\"%s\"}",
				center.x, center.y, center.z, radius, source));
		}
	}
}
