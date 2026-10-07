package dev.daolyap.mineheim.client.mixin;

import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.ClientInput;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Every swing Minecraft makes while a host is attached also lands in the host's world (ClientInput.swing). */
@Mixin(Minecraft.class)
abstract class MinecraftAttackMixin {
	@Inject(method = "startAttack", at = @At("HEAD"))
	private void mineheim$hostSwing(final CallbackInfoReturnable<Boolean> cir) {
		if (MineHeim.active) {
			ClientInput.swing((Minecraft) (Object) this);
		}
	}
}
