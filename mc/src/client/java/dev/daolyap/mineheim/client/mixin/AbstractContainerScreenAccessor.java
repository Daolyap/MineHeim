package dev.daolyap.mineheim.client.mixin;

import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Where an inventory screen's panel is, so the Viking's bag can sit next to it. */
@Mixin(AbstractContainerScreen.class)
public interface AbstractContainerScreenAccessor {
	@Accessor("leftPos")
	int mineheim$leftPos();

	@Accessor("topPos")
	int mineheim$topPos();

	@Accessor("imageWidth")
	int mineheim$imageWidth();

	@Accessor("imageHeight")
	int mineheim$imageHeight();
}
