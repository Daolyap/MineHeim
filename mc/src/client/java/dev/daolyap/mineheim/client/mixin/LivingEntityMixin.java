package dev.daolyap.mineheim.client.mixin;

import dev.daolyap.mineheim.client.HostState;
import dev.daolyap.mineheim.client.PlayerSync;
import dev.daolyap.mineheim.client.SkinSender;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.component.SwingAnimation;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The local player is placed every frame, so its own tick movement is ~0: animate the walk from the host's movement.
 * Its arm swings (attack, mining, placing, using) go to the host, which draws Steve itself.
 */
@Mixin(LivingEntity.class)
abstract class LivingEntityMixin {
	@Inject(method = "swing(Lnet/minecraft/world/InteractionHand;Lnet/minecraft/world/item/component/SwingAnimation;Z)Z", at = @At("HEAD"))
	private void mineheim$hostSwing(final InteractionHand hand, final SwingAnimation animation, final boolean updateSelf, final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.frame() != null && (Object) this == Minecraft.getInstance().player) {
			SkinSender.armSwing(hand == InteractionHand.OFF_HAND);
		}
	}

	@Inject(method = "updateWalkAnimation", at = @At("HEAD"), cancellable = true)
	private void mineheim$hostWalk(final float distance, final CallbackInfo ci) {
		if ((Object) this instanceof LocalPlayer player && HostState.frame() != null) {
			player.walkAnimation.update(Math.min(PlayerSync.tickDistance() * 4.0F, 1.0F), 0.4F, 1.0F);
			ci.cancel();
		}
	}
}
