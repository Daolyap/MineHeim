package dev.daolyap.mineheim.client.mixin;

import dev.daolyap.mineheim.client.ValheimBag;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** A Valheim stand-in on Steve's hotbar or in an inventory is drawn with the Valheim item's own icon. */
@Mixin(GuiGraphicsExtractor.class)
abstract class GuiGraphicsExtractorMixin {
	@Inject(method = "item(Lnet/minecraft/world/entity/LivingEntity;Lnet/minecraft/world/level/Level;Lnet/minecraft/world/item/ItemStack;III)V", at = @At("HEAD"), cancellable = true)
	private void mineheim$valheimIcon(final LivingEntity entity, final Level level, final ItemStack stack, final int x, final int y, final int seed, final CallbackInfo ci) {
		if (ValheimBag.drawStandIn((GuiGraphicsExtractor) (Object) this, stack, x, y)) {
			ci.cancel();
		}
	}
}
