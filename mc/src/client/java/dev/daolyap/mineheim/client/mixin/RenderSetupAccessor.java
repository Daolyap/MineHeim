package dev.daolyap.mineheim.client.mixin;

import java.util.Map;
import net.minecraft.client.renderer.rendertype.RenderSetup;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** A render setup's textures by sampler name ("Sampler0": the main one); the values are its TextureBinding records. */
@Mixin(RenderSetup.class)
public interface RenderSetupAccessor {
	@Accessor("textures")
	Map<String, ?> mineheim$textures();
}
