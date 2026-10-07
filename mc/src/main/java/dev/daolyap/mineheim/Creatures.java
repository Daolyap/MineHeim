package dev.daolyap.mineheim;

import com.google.gson.JsonArray;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.phys.AABB;

/**
 * Minecraft's mobs and the host's creatures in one fight:
 * <ul>
 * <li>the host's creatures stand here as invisible CreatureProxy entities ({"t":"creatures"} from the host, ~10 a second)</li>
 * <li>Minecraft's monsters with nothing to do hunt the nearest one they can see</li>
 * <li>Minecraft's mobs near the player are reported to the host ({"t":"mobs"}, 5 a second) so they stand in the host's
 * world too, and the host's hits on them come back as {"t":"mhit"}</li>
 * </ul>
 */
public final class Creatures {
	private static final Map<Integer, CreatureProxy> proxies = new HashMap<>(); // server thread only
	/** The host's switch (Mobs.Fight): whether Minecraft's monsters go after the host's creatures. */
	public static volatile boolean fight = true;
	private static int ticks;
	private static boolean reportedNone = true;

	private Creatures() {
	}

	/** {"t":"creatures","c":[[id,"name",x,y,z,width,height],...]}: every creature of the host near its player, feet positions. */
	public static void update(final JsonArray list) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			Set<Integer> seen = new HashSet<>();
			for (int i = 0; i < list.size(); i++) {
				JsonArray c = list.get(i).getAsJsonArray();
				int id = c.get(0).getAsInt();
				double x = c.get(2).getAsDouble(), y = c.get(3).getAsDouble(), z = c.get(4).getAsDouble();
				seen.add(id);
				CreatureProxy p = proxies.get(id);
				if (p == null || p.isRemoved()) {
					p = CreatureProxy.TYPE.create(level, EntitySpawnReason.COMMAND);
					if (p == null) {
						continue;
					}

					p.hostId = id;
					p.hostName = c.get(1).getAsString();
					p.setPos(x, y, z);
					p.setSize(c.get(5).getAsFloat(), c.get(6).getAsFloat());
					level.addFreshEntity(p);
					proxies.put(id, p);
				} else {
					p.setPos(x, y, z);
					p.setSize(c.get(5).getAsFloat(), c.get(6).getAsFloat());
				}
			}

			for (Iterator<Map.Entry<Integer, CreatureProxy>> it = proxies.entrySet().iterator(); it.hasNext();) {
				Map.Entry<Integer, CreatureProxy> e = it.next();
				if (!seen.contains(e.getKey())) {
					e.getValue().discard();
					it.remove();
				}
			}
		});
	}

	/** The host went away (or the world stops): its creatures go with it. */
	public static void clear() {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			proxies.clear();
			return;
		}

		s.execute(() -> {
			proxies.values().forEach(Entity::discard);
			proxies.clear();
		});
	}

	static void detach() {
		proxies.clear();
	}

	public static int count() {
		return proxies.size();
	}

	/** Every server tick. */
	static void tick(final MinecraftServer s) {
		ticks++;
		ServerLevel level = s.overworld();
		if (fight && !proxies.isEmpty() && ticks % 10 == 0) {
			hunt(level);
		}

		if (MineHeim.active && ticks % 4 == 0) {
			reportMobs(s, level);
		}
	}

	/** Monsters that aren't busy go for the nearest of the host's creatures they can see. */
	private static void hunt(final ServerLevel level) {
		for (CreatureProxy p : proxies.values()) {
			if (p.isRemoved()) {
				continue;
			}

			List<Mob> near = level.getEntitiesOfClass(Mob.class, p.getBoundingBox().inflate(16.0), m -> m instanceof Enemy && !(m instanceof CreatureProxy) && m.isAlive());
			for (Mob m : near) {
				LivingEntity target = m.getTarget();
				if (target != null && target.isAlive() && !(target instanceof CreatureProxy && target.isRemoved())) {
					continue;
				}

				if (m.getSensing().hasLineOfSight(p)) {
					m.setTarget(p);
				}
			}
		}
	}

	/** {"t":"mobs","m":[[id,"zombie",x,y,z,width,height,health,maxHealth,hostile 0|1],...]}: Minecraft's mobs near the player. */
	private static void reportMobs(final MinecraftServer s, final ServerLevel level) {
		List<ServerPlayer> players = s.getPlayerList().getPlayers();
		if (players.isEmpty()) {
			return;
		}

		ServerPlayer player = players.get(0);
		List<Mob> mobs = level.getEntitiesOfClass(Mob.class, new AABB(player.blockPosition()).inflate(40.0), m -> !(m instanceof CreatureProxy) && m.isAlive());
		if (mobs.isEmpty() && reportedNone) {
			return;
		}

		reportedNone = mobs.isEmpty();
		StringBuilder b = new StringBuilder("{\"t\":\"mobs\",\"m\":[");
		boolean first = true;
		for (Mob m : mobs) {
			b.append(first ? "" : ",").append(String.format(Locale.ROOT, "[%d,\"%s\",%.3f,%.3f,%.3f,%.2f,%.2f,%.1f,%.1f,%d]",
				m.getId(), BuiltInRegistries.ENTITY_TYPE.getKey(m.getType()).getPath(), m.getX(), m.getY(), m.getZ(),
				m.getBbWidth(), m.getBbHeight(), m.getHealth(), m.getMaxHealth(), m instanceof Enemy ? 1 : 0));
			first = false;
		}

		MineHeim.events.accept(b.append("]}").toString());
	}

	/**
	 * {"t":"mhit","id":mob,"amount":hp,"by":host creature id or -1,"player":bool}: the host hurt one of Minecraft's mobs (its
	 * creature or its player with a weapon of its own). The mob takes it as a hit from that creature's proxy or from the
	 * player, so it fights back.
	 */
	public static void hitMob(final int id, final float amount, final int by, final boolean byPlayer) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			Entity e = level.getEntity(id);
			if (!(e instanceof Mob mob) || !mob.isAlive() || amount <= 0F) {
				return;
			}

			DamageSource source;
			CreatureProxy attacker = by >= 0 ? proxies.get(by) : null;
			List<ServerPlayer> players = s.getPlayerList().getPlayers();
			if (byPlayer && !players.isEmpty()) {
				source = level.damageSources().playerAttack(players.get(0));
			} else if (attacker != null && !attacker.isRemoved()) {
				source = level.damageSources().mobAttack(attacker);
			} else {
				source = level.damageSources().generic();
			}

			mob.hurtServer(level, source, amount);
		});
	}
}
