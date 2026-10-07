package dev.daolyap.mineheim.client.mixin;

import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.HostView;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.client.renderer.entity.EntityRenderer;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.world.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * While a host is attached: Steve is drawn only in the host's Minecraft mode, and mobs and items resting on the host's
 * (whole-block) barrier ground are drawn standing on the host's real surface instead of up to a block inside it.
 */
@Mixin(EntityRenderer.class)
abstract class EntityRendererMixin {
	@Inject(method = "shouldRender", at = @At("HEAD"), cancellable = true)
	private void mineheim$steveOnlyInMinecraftMode(final Entity entity, final Frustum frustum, final double x, final double y, final double z,
		final float partialTicks, final CallbackInfoReturnable<Boolean> cir) {
		if (MineHeim.active && !HostView.steve && entity == Minecraft.getInstance().player) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "extractRenderState", at = @At("TAIL"))
	private void mineheim$onHostGround(final Entity entity, final EntityRenderState state, final float partialTicks, final CallbackInfo ci) {
		if (MineHeim.active && entity != Minecraft.getInstance().player && (entity.onGround() || entity.getDeltaMovement().lengthSqr() < 1e-4)) {
			state.y = HostView.groundY(state.x, state.y, state.z);
		}
	}
}
