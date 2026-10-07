package dev.daolyap.mineheim.mixin;

import dev.daolyap.mineheim.CreatureProxy;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.Projectile;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The player's arrows fly through the host's creatures here: the host traces them into the creature itself ("proj"). */
@Mixin(Projectile.class)
abstract class ProjectileMixin {
	@Inject(method = "canHitEntity", at = @At("HEAD"), cancellable = true)
	private void mineheim$notThroughProxies(final Entity target, final CallbackInfoReturnable<Boolean> cir) {
		if (target instanceof CreatureProxy && ((Projectile) (Object) this).getOwner() instanceof Player) {
			cir.setReturnValue(false);
		}
	}
}
