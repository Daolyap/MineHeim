package dev.daolyap.mineheim.mixin;

import dev.daolyap.mineheim.MineHeim;
import java.util.Locale;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.Consumable;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(FoodProperties.class)
abstract class FoodPropertiesMixin {
	/**
	 * Steve ate something: the host's player eats it as a food of its own ({"t":"ate","item":"bread","n":nutrition}).
	 * Minecraft's hunger bar only shows the host's food, so what Minecraft adds here is overwritten by the next sync.
	 */
	@Inject(method = "onConsume", at = @At("HEAD"))
	private void mineheim$ate(final Level level, final LivingEntity entity, final ItemStack stack, final Consumable consumable, final CallbackInfo ci) {
		if (MineHeim.active && !level.isClientSide() && entity instanceof ServerPlayer) {
			FoodProperties self = (FoodProperties) (Object) this;
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"ate\",\"item\":\"%s\",\"n\":%d}",
				BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath(), self.nutrition()));
		}
	}
}
