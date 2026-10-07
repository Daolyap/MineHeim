package dev.daolyap.mineheim.mixin;

import dev.daolyap.mineheim.MineHeim;
import net.minecraft.world.entity.Mob;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Minecraft's sun follows the host's day (only for its light): undead don't burn in it. */
@Mixin(Mob.class)
abstract class MobMixin {
	@Inject(method = "burnUndead()V", at = @At("HEAD"), cancellable = true)
	private void mineheim$noSunburn(final CallbackInfo ci) {
		if (MineHeim.active) {
			ci.cancel();
		}
	}
}
