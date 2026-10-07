package dev.daolyap.mineheim.mixin;

import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.block.Portal;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Only this world is the host's: nothing goes through portals. On the client
 * the usual portal overlay and sound still play.
 */
@Mixin(Entity.class)
abstract class PortalMixin {
	@Inject(method = "setAsInsidePortal(Lnet/minecraft/world/level/block/Portal;Lnet/minecraft/core/BlockPos;)V", at = @At("HEAD"), cancellable = true)
	private void mineheim$noTravel(final Portal portal, final BlockPos pos, final CallbackInfo ci) {
		if (!((Entity) (Object) this).level().isClientSide()) {
			ci.cancel();
		}
	}
}
