package dev.daolyap.mineheim.mixin;

import dev.daolyap.mineheim.MineHeim;
import java.util.Locale;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.tags.DamageTypeTags;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Relative;
import net.minecraft.world.level.portal.TeleportTransition;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(ServerPlayer.class)
abstract class ServerPlayerMixin {
	/** Minecraft moving the player (ender pearls, /tp) moves the host's player too; otherwise the host would pull it back. */
	@Inject(method = "teleport(Lnet/minecraft/world/level/portal/TeleportTransition;)Lnet/minecraft/server/level/ServerPlayer;", at = @At("HEAD"))
	private void mineheim$teleported(final TeleportTransition transition, final CallbackInfoReturnable<ServerPlayer> cir) {
		if (MineHeim.active) {
			// a teleport may come as an offset per axis (Relative.X/Y/Z): the host wants where the player ends up
			Entity self = (Entity) (Object) this;
			Vec3 t = transition.position();
			Vec3 p = new Vec3(
				transition.relatives().contains(Relative.X) ? self.getX() + t.x : t.x,
				transition.relatives().contains(Relative.Y) ? self.getY() + t.y : t.y,
				transition.relatives().contains(Relative.Z) ? self.getZ() + t.z : t.z);
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", p.x, p.y, p.z));
		}
	}

	/**
	 * The host owns the player's health: Minecraft never applies damage to Steve. A hit by a mob (melee or its projectile)
	 * goes to the host as {"t":"hurt","amount":hp,"src":"zombie","pos":[x,y,z]} so the host's own player takes it (and can
	 * block it there). Fire, lava and magma blocks burn it outside Creative ("fire":true). Explosions are not sent: the host already takes those through the "explosion" event. Falls, the void,
	 * suffocation and the like are not sent either: the host moves the player, Minecraft's physics never decide its fate.
	 */
	@Inject(method = "hurtServer", at = @At("HEAD"), cancellable = true)
	private void mineheim$hostOwnsHealth(final ServerLevel level, final DamageSource source, final float amount, final CallbackInfoReturnable<Boolean> cir) {
		if (!MineHeim.active) {
			return;
		}

		Entity self = (Entity) (Object) this;
		Entity attacker = source.getEntity();
		if (attacker != null && attacker != self && amount > 0f && !source.is(DamageTypeTags.IS_EXPLOSION)) {
			Vec3 from = source.getSourcePosition() != null ? source.getSourcePosition() : attacker.position();
			String src = BuiltInRegistries.ENTITY_TYPE.getKey(attacker.getType()).getPath();
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"hurt\",\"amount\":%.2f,\"src\":\"%s\",\"pos\":[%.3f,%.3f,%.3f]}",
				amount, src, from.x, from.y, from.z));
		} else if (attacker == null && amount > 0f && source.is(DamageTypeTags.IS_FIRE) && !((ServerPlayer) (Object) this).isCreative()) {
			// fire, lava, a magma block (the Nether infection's ground): the host's player burns, as fire damage
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"hurt\",\"amount\":%.2f,\"src\":\"%s\",\"fire\":true,\"pos\":[%.3f,%.3f,%.3f]}",
				amount, source.getMsgId(), self.getX(), self.getY(), self.getZ()));
		}

		cir.setReturnValue(false);
	}
}
