package dev.daolyap.mineheim.client.mixin;

import com.mojang.blaze3d.pipeline.RenderTarget;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.FrameExporter;
import dev.daolyap.mineheim.client.HostView;
import net.minecraft.client.renderer.GameRenderer;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** No sky while a host is attached (the host's sky shows through), and the frame is split into world and overlay layers. */
@Mixin(GameRenderer.class)
abstract class GameRendererMixin {
	@Shadow @Final private RenderTarget mainRenderTarget;

	@ModifyArg(
		method = "renderLevel",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/LevelRenderer;render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lcom/mojang/renderpearl/api/buffers/GpuBufferSlice;Lorg/joml/Vector4f;ZZ)V"),
		index = 5
	)
	private boolean mineheim$noSky(final boolean shouldRenderSky) {
		return shouldRenderSky && !MineHeim.active;
	}

	@Inject(
		method = "renderLevel",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/GameRenderer;render3dHud(Lnet/minecraft/client/renderer/state/level/CameraRenderState;Lnet/minecraft/client/renderer/state/level/PlayerRenderState;Lnet/minecraft/client/renderer/state/OptionsRenderState;Z)V")
	)
	private void mineheim$captureWorld(final CallbackInfo ci) {
		FrameExporter.captureWorld(this.mainRenderTarget);
	}

	/** Steve's hand only in the host's Minecraft mode (out of it the host's own player holds what it holds). */
	@Inject(method = "renderItemInHand", at = @At("HEAD"), cancellable = true)
	private void mineheim$handOnlyAsSteve(final CallbackInfo ci) {
		if (MineHeim.active && !HostView.steve) {
			ci.cancel();
		}
	}

	@Inject(method = "render", at = @At("TAIL"))
	private void mineheim$captureOverlay(final CallbackInfo ci) {
		FrameExporter.captureOverlay(this.mainRenderTarget);
	}
}
