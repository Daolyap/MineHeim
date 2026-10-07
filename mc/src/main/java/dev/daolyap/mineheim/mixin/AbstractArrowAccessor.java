package dev.daolyap.mineheim.mixin;

import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** An arrow's damage per unit of speed (2 for a plain bow shot, more with Power): Minecraft hits for ceil(speed x this). */
@Mixin(AbstractArrow.class)
public interface AbstractArrowAccessor {
	@Accessor("baseDamage")
	double mineheim$getBaseDamage();
}
