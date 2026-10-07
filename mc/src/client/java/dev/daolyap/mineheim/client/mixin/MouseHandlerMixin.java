package dev.daolyap.mineheim.client.mixin;

import dev.daolyap.mineheim.MineHeim;
import net.minecraft.client.Minecraft;
import net.minecraft.client.MouseHandler;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * Minecraft only passes the cursor's movement to an open screen (hover, dragging items across slots) while its window
 * has the focus. Attached to a host, its window never has it: the host forwards the cursor instead, so let it through.
 */
@Mixin(MouseHandler.class)
abstract class MouseHandlerMixin {
	@Redirect(method = "handleAccumulatedMovement", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/Minecraft;isWindowActive()Z"))
	private boolean mineheim$hostCursor(final Minecraft minecraft) {
		return MineHeim.active || minecraft.isWindowActive();
	}
}
