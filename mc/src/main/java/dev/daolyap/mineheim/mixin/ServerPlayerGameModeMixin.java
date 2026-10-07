package dev.daolyap.mineheim.mixin;

import dev.daolyap.mineheim.MineHeim;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayerGameMode;
import net.minecraft.world.level.block.Blocks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The host's ground arrives as barrier blocks, and a Creative player can break barriers with one click: aiming a pickaxe at
 * the ground (to dig the host's terrain) would punch holes into Steve's floor. Players never break barriers while a host is
 * connected; the server tells the client the block is still there.
 */
@Mixin(ServerPlayerGameMode.class)
abstract class ServerPlayerGameModeMixin {
	@Shadow
	protected ServerLevel level;

	@Inject(method = "destroyBlock", at = @At("HEAD"), cancellable = true)
	private void mineheim$keepGround(final BlockPos pos, final CallbackInfoReturnable<Boolean> cir) {
		if (MineHeim.active && this.level.getBlockState(pos).is(Blocks.BARRIER)) {
			cir.setReturnValue(false);
		}
	}
}
