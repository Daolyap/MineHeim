package dev.daolyap.mineheim;

import java.util.function.Consumer;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class MineHeim implements ModInitializer {
	public static final String ID = "mineheim";
	public static final Logger LOG = LoggerFactory.getLogger(ID);
	/** True while a host game is driving the camera. The integrated server shares this JVM, so both sides read it. */
	public static volatile boolean active;
	/** Where events for the host go (JSON lines); the client's HostLink sets it. */
	public static volatile Consumer<String> events = message -> {};

	@Override
	public void onInitialize() {
		CreatureProxy.register();
		FabricDefaultAttributeRegistry.register(CreatureProxy.TYPE, CreatureProxy.attributes());
		ServerLifecycleEvents.SERVER_STARTED.register(WorldBridge::attach);
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> WorldBridge.detach());
		ServerTickEvents.END_SERVER_TICK.register(WorldBridge::tick);
		ServerTickEvents.END_SERVER_TICK.register(Creatures::tick);
		LOG.info("mineheim loaded");
	}
}
