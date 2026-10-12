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
import net.minecraft.world.entity.NeutralMob;
import net.minecraft.world.entity.ai.Brain;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.ai.memory.MemoryModuleType;
import net.minecraft.world.entity.ai.memory.MemoryStatus;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.projectile.hurtingprojectile.AbstractHurtingProjectile;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.EntityHitResult;
import dev.daolyap.mineheim.mixin.ProjectileInvoker;

/**
 * Minecraft's mobs and the host's creatures in one fight:
 * <ul>
 * <li>the host's creatures stand here as invisible CreatureProxy entities ({"t":"creatures"} from the host, ~10 a second)</li>
 * <li>Minecraft's monsters with nothing to do hunt the nearest one they can see</li>
 * <li>a mob's fireball (a ghast's, a blaze's) that comes close to one bursts on it</li>
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

		if (!proxies.isEmpty()) {
			fuse(level);
		}

		if (MineHeim.active && ticks % 4 == 0) {
			reportMobs(s, level);
		}
	}

	/**
	 * Monsters that aren't busy go for the nearest of the host's creatures they can see, as far as they would notice a
	 * player (a zombie 35 blocks, a ghast 64 and more). Neutral mobs (zombified piglins, endermen) only fight back, as in
	 * Minecraft. Mobs run by a brain (piglins, hoglins...) get it as their brain's target, the way they pick a player.
	 */
	private static void hunt(final ServerLevel level) {
		Map<Mob, CreatureProxy> best = new HashMap<>();
		Map<Mob, Double> bestDistance = new HashMap<>();
		for (CreatureProxy p : proxies.values()) {
			if (p.isRemoved()) {
				continue;
			}

			List<Mob> near = level.getEntitiesOfClass(Mob.class, p.getBoundingBox().inflate(HUNT_RANGE),
				m -> m instanceof Enemy && !(m instanceof NeutralMob) && !(m instanceof CreatureProxy) && m.isAlive());
			for (Mob m : near) {
				double d = m.distanceToSqr(p), range = m.getAttributeValue(Attributes.FOLLOW_RANGE);
				if (d > range * range || busy(m) || d >= bestDistance.getOrDefault(m, Double.MAX_VALUE) || !m.getSensing().hasLineOfSight(p)) {
					continue;
				}

				best.put(m, p);
				bestDistance.put(m, d);
			}
		}

		best.forEach(Creatures::attack);
	}

	/** How close a mob's fireball must pass a creature's box to burst on it, blocks. */
	private static final double FUSE = 1.5;

	/**
	 * A mob's fireball that passes close to one of the host's creatures bursts on it. A ghast aims where a creature was, and
	 * the host's flyers (a Gjall, Deathsquitos) drift and only move here 10 times a second: a fireball that needed to touch
	 * the box exactly would sail past nearly every time and burst far away on the ground. Hitting it makes the fireball do
	 * what it does to a player: its impact (sent to the host as a hit) and its blast (the host blasts what is in reach).
	 */
	private static void fuse(final ServerLevel level) {
		for (CreatureProxy p : proxies.values()) {
			if (p.isRemoved()) {
				continue;
			}

			AABB reach = p.getBoundingBox().inflate(FUSE);
			for (AbstractHurtingProjectile f : level.getEntitiesOfClass(AbstractHurtingProjectile.class, reach,
				f -> f.isAlive() && f.getOwner() instanceof Mob owner && !(owner instanceof CreatureProxy) && f.tickCount > 2)) {
				MineHeim.LOG.info("{} from {} burst on the host's {}", BuiltInRegistries.ENTITY_TYPE.getKey(f.getType()).getPath(),
					BuiltInRegistries.ENTITY_TYPE.getKey(f.getOwner().getType()).getPath(), p.hostName);
				((ProjectileInvoker) f).mineheim$onHit(new EntityHitResult(p));
			}
		}
	}

	/** How far around a creature to look for monsters: the farthest any mob notices a target (a ghast). */
	private static final double HUNT_RANGE = 64.0;

	/** Whether the mob already has a live target (a goal's, or its brain's). */
	private static boolean busy(final Mob m) {
		LivingEntity target = m.getTarget();
		if (target != null && target.isAlive() && !target.isRemoved()) {
			return true;
		}

		Brain<?> brain = m.getBrain();
		return brain.checkMemory(MemoryModuleType.ATTACK_TARGET, MemoryStatus.REGISTERED)
			&& brain.getMemory(MemoryModuleType.ATTACK_TARGET).filter(t -> t.isAlive() && !t.isRemoved()).isPresent();
	}

	private static void attack(final Mob m, final CreatureProxy p) {
		m.setTarget(p);
		Brain<?> brain = m.getBrain();
		if (brain.checkMemory(MemoryModuleType.ATTACK_TARGET, MemoryStatus.REGISTERED)) {
			brain.setMemory(MemoryModuleType.ATTACK_TARGET, p);
			if (brain.checkMemory(MemoryModuleType.ANGRY_AT, MemoryStatus.REGISTERED)) {
				brain.setMemoryWithExpiry(MemoryModuleType.ANGRY_AT, p.getUUID(), 600L); // piglins keep to it (their target must be who they are angry at)
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
