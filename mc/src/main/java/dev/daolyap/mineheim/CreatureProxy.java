package dev.daolyap.mineheim;

import java.util.Locale;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.EntityDataSerializers;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.tags.DamageTypeTags;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityDimensions;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.PathfinderMob;
import net.minecraft.world.entity.Pose;
import net.minecraft.world.entity.ai.attributes.AttributeSupplier;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.Vec3;

/**
 * One of the host's creatures (a Greydwarf, a Troll, a deer...) standing in this world: invisible, silent and without a
 * will of its own, it goes where the host's creature goes and has its size. Minecraft's monsters hunt it and its hits
 * go to the host's creature ({"t":"chit"}); it never loses health here, the host owns that.
 *
 * <p>The player's own hits never land on it: Steve's swings and arrows already reach the host's creature through the
 * host ("melee", "proj"), so on the client it cannot be picked and the player's projectiles fly through it.
 */
public class CreatureProxy extends PathfinderMob implements Enemy {
	public static final ResourceKey<EntityType<?>> KEY = ResourceKey.create(Registries.ENTITY_TYPE, Identifier.fromNamespaceAndPath(MineHeim.ID, "creature"));
	public static EntityType<CreatureProxy> TYPE;

	private static final EntityDataAccessor<Float> WIDTH = SynchedEntityData.defineId(CreatureProxy.class, EntityDataSerializers.FLOAT);
	private static final EntityDataAccessor<Float> HEIGHT = SynchedEntityData.defineId(CreatureProxy.class, EntityDataSerializers.FLOAT);

	/** The host's id for its creature. */
	public int hostId;
	public String hostName = "creature";

	public CreatureProxy(final EntityType<? extends CreatureProxy> type, final Level level) {
		super(type, level);
		this.setNoAi(true);
		this.setNoGravity(true);
		this.setSilent(true);
		this.setInvisible(true);
		this.setPersistenceRequired();
		this.noPhysics = true;
	}

	static void register() {
		TYPE = Registry.register(BuiltInRegistries.ENTITY_TYPE, KEY,
			EntityType.Builder.<CreatureProxy>of(CreatureProxy::new, MobCategory.MISC).sized(0.6F, 1.8F).noSave().noSummon().noLootTable().clientTrackingRange(8).build(KEY));
	}

	public static AttributeSupplier.Builder attributes() {
		return PathfinderMob.createMobAttributes().add(Attributes.MAX_HEALTH, 1000.0);
	}

	@Override
	protected void defineSynchedData(final SynchedEntityData.Builder builder) {
		super.defineSynchedData(builder);
		builder.define(WIDTH, 0.6F);
		builder.define(HEIGHT, 1.8F);
	}

	/** The host's creature's size (metres = blocks). */
	public void setSize(final float width, final float height) {
		float w = Math.max(0.2F, Math.min(width, 8F)), h = Math.max(0.2F, Math.min(height, 12F));
		if (Math.abs(w - this.entityData.get(WIDTH)) > 0.05F || Math.abs(h - this.entityData.get(HEIGHT)) > 0.05F) {
			this.entityData.set(WIDTH, w);
			this.entityData.set(HEIGHT, h);
			this.refreshDimensions();
		}
	}

	@Override
	public void onSyncedDataUpdated(final EntityDataAccessor<?> accessor) {
		super.onSyncedDataUpdated(accessor);
		if (WIDTH.equals(accessor) || HEIGHT.equals(accessor)) {
			this.refreshDimensions();
		}
	}

	@Override
	protected EntityDimensions getDefaultDimensions(final Pose pose) {
		return EntityDimensions.scalable(this.entityData.get(WIDTH), this.entityData.get(HEIGHT));
	}

	@Override
	public void tick() {
		this.noPhysics = true;
		super.tick();
	}

	/**
	 * A Minecraft mob's hit (melee, or its arrow or fireball) goes to the host's creature. Explosions are not sent: the host
	 * already blasts every creature in reach. Fire, falls and the like never reach it: the host's creature lives there.
	 */
	@Override
	public boolean hurtServer(final ServerLevel level, final DamageSource source, final float amount) {
		Entity attacker = source.getEntity();
		if (!MineHeim.active || amount <= 0F || !(attacker instanceof LivingEntity) || attacker instanceof Player || attacker instanceof CreatureProxy
			|| source.is(DamageTypeTags.IS_EXPLOSION)) {
			return false;
		}

		Vec3 from = attacker.position();
		boolean ranged = source.getDirectEntity() != null && source.getDirectEntity() != attacker;
		String src = BuiltInRegistries.ENTITY_TYPE.getKey(attacker.getType()).getPath();
		MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"chit\",\"id\":%d,\"amount\":%.2f,\"src\":\"%s\",\"by\":%d,\"ranged\":%b,\"pos\":[%.3f,%.3f,%.3f]}",
			this.hostId, amount, src, attacker.getId(), ranged, from.x, from.y, from.z));
		this.setLastHurtByMob((LivingEntity) attacker);
		return true;
	}

	/** On the client nothing can aim at it (Steve's hits reach the host's creature through the host); on the server mobs' arrows can. */
	@Override
	public boolean isPickable() {
		return !this.level().isClientSide();
	}

	@Override
	public boolean isPushable() {
		return false;
	}

	@Override
	protected void pushEntities() {
	}

	@Override
	protected void doPush(final Entity entity) {
	}

	@Override
	public boolean isAffectedByPotions() {
		return false;
	}

	@Override
	public boolean removeWhenFarAway(final double distance) {
		return false;
	}
}
