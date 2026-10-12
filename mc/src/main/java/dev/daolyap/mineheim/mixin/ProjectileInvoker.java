package dev.daolyap.mineheim.mixin;

import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.phys.HitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** A projectile's own impact (what it does when it hits something): a mob's fireball bursting at one of the host's creatures. */
@Mixin(Projectile.class)
public interface ProjectileInvoker {
	@Invoker("onHit")
	void mineheim$onHit(HitResult hit);
}
