package dev.daolyap.mineheim.client;

import com.google.gson.JsonObject;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.mixin.KeyMappingAccessor;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.client.player.LocalPlayer;
import java.util.Locale;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.player.Inventory;
import org.lwjgl.sdl.SDLVideo;

/** Host input, applied on the client thread: the host window has the focus, so Minecraft never sees these itself. */
final class ClientInput {
	private ClientInput() {
	}

	/** The player's attack damage with what it holds (the server's copy: the held item's bonus is applied there). */
	private static double attackDamage(final LocalPlayer player) {
		MinecraftServer server = minecraft().getSingleplayerServer();
		ServerPlayer p = server != null ? server.getPlayerList().getPlayer(player.getUUID()) : null;
		return p != null ? p.getAttributeValue(Attributes.ATTACK_DAMAGE) : player.getAttributeValue(Attributes.ATTACK_DAMAGE);
	}

	private static Minecraft minecraft() {
		return Minecraft.getInstance();
	}

	/** SDL3 modifier bits from the host (0x1 Shift, 0x40 Ctrl, 0x100 Alt): shift-click and the like read them off the event. */
	private static int mods(final JsonObject m) {
		return m.has("mods") ? m.get("mods").getAsInt() : 0;
	}

	private static void click(final KeyMapping key) {
		KeyMappingAccessor access = (KeyMappingAccessor) key;
		access.mineheim$setClickCount(access.mineheim$getClickCount() + 1);
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		LocalPlayer player = minecraft.player;
		long window = minecraft.getWindow().handle();
		switch (m.get("t").getAsString()) {
			// Minecraft's own screens (inventory, creative tabs, chat, chests, crafting) worked from the host's window: the host
			// forwards the cursor, buttons, wheel, keys and typed text while one is open; Minecraft handles them as its own input
			case "screenopen" -> {
				if (player != null && minecraft.gui.screen() == null) {
					switch (m.get("k").getAsString()) {
						case "inventory" -> click(minecraft.options.keyInventory);
						case "chat" -> minecraft.gui.setScreen(new ChatScreen("", false));
						case "command" -> minecraft.gui.setScreen(new ChatScreen("/", false));
						default -> {
						}
					}
				}
			}
			case "mmove" -> {
				double x = m.get("x").getAsDouble(), y = m.get("y").getAsDouble();
				minecraft.mouseHandler.onMove(window, x, y, x - minecraft.mouseHandler.xpos(), y - minecraft.mouseHandler.ypos());
			}
			case "mbtn" -> {
				// straight to the open screen, at the cursor the host forwarded (scaled to the GUI as Minecraft does)
				Screen screen = minecraft.gui.screen();
				if (screen == null) {
					return;
				}

				double x = MouseHandler.getScaledXPos(minecraft.getWindow(), minecraft.mouseHandler.xpos());
				double y = MouseHandler.getScaledYPos(minecraft.getWindow(), minecraft.mouseHandler.ypos());
				MouseButtonEvent event = new MouseButtonEvent(x, y, new MouseButtonInfo(m.get("b").getAsInt(), mods(m)));
				boolean handled = m.get("down").getAsBoolean() ? screen.mouseClicked(event, false) : screen.mouseReleased(event);
				screen.afterMouseAction();
				MineHeim.LOG.info("screen {} button {} {} at {},{}: {}", screen.getClass().getSimpleName(), m.get("b").getAsInt(),
					m.get("down").getAsBoolean() ? "down" : "up", (int) x, (int) y, handled);
			}
			case "mscroll" -> minecraft.mouseHandler.onScroll(window, 0.0, m.get("d").getAsDouble());
			case "kpress" -> minecraft.keyboardHandler.keyPress(window, m.get("down").getAsBoolean() ? 1 : 0, new KeyEvent(m.get("k").getAsInt(), 0, mods(m)));
			case "char" -> minecraft.keyboardHandler.charTyped(window, new CharacterEvent(m.get("c").getAsInt()));
			case "window" -> {
				// the host shows Minecraft's picture: its own window stays out of the way (parked off-screen: it keeps rendering)
				WindowParking.set(window, m.get("hidden").getAsBoolean());
			}
			case "quit" -> {
				// the host is closing: save and quit with it
				MineHeim.LOG.info("the host is closing: quitting Minecraft");
				minecraft.stop();
			}
			case "key" -> {
				String k = m.get("k").getAsString();
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				if (k.equals("drop")) {
					// thrown on the server side: the drop key's Ctrl (whole stack) is read from this window, which never has the focus
					boolean stack = m.has("stack") && m.get("stack").getAsBoolean();
					MinecraftServer server = minecraft.getSingleplayerServer();
					if (down && player != null && server != null) {
						java.util.UUID id = player.getUUID();
						server.execute(() -> {
							ServerPlayer p = server.getPlayerList().getPlayer(id);
							if (p != null) {
								p.drop(stack);
							}
						});
					}

					return;
				}

				if (k.equals("escape")) {
					if (down && minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
					}

					return;
				}

				KeyMapping key = switch (k) {
					case "use" -> minecraft.options.keyUse;
					case "attack" -> minecraft.options.keyAttack;
					case "pick" -> minecraft.options.keyPickItem;
					case "inventory" -> minecraft.options.keyInventory;
					case "drop" -> minecraft.options.keyDrop;
					case "swap" -> minecraft.options.keySwapOffhand;
					default -> null;
				};
				if (k.equals("attack") && down && player != null) {
					String item = BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath();
					if (item.endsWith("_sword") || item.endsWith("_pickaxe") || item.endsWith("_axe")) {
						// a swing: the host hits what's in front of Steve in its own world (creatures for a sword, rocks and trees for tools),
						// for what the swing would deal here: the weapon's attack damage times how far the attack has recharged
						double damage = attackDamage(player) * player.getAttackStrengthScale(0.5F);
						MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"melee\",\"item\":\"%s\",\"dmg\":%.2f}", item, damage));
					}
				}

				if (key != null) {
					if (down && !key.isDown()) {
						KeyMappingAccessor access = (KeyMappingAccessor)key;
						access.mineheim$setClickCount(access.mineheim$getClickCount() + 1);
					}

					key.setDown(down);
				}
			}
			case "slot" -> {
				if (player != null) {
					player.getInventory().setSelectedSlot(Math.clamp(m.get("n").getAsInt(), 0, Inventory.getSelectionSize() - 1));
				}
			}
			case "scroll" -> {
				if (player != null) {
					Inventory inventory = player.getInventory();
					int size = Inventory.getSelectionSize();
					inventory.setSelectedSlot(Math.floorMod(inventory.getSelectedSlot() - m.get("d").getAsInt(), size));
				}
			}
			case "hud" -> {
				if (minecraft.gui.hud.isHidden() != m.get("hidden").getAsBoolean()) {
					minecraft.gui.hud.toggle();
				}
			}
			case "view" -> {
				// match the host's picture exactly: un-minimize/un-maximize first (resizing a maximized window is ignored)
				int w = m.get("w").getAsInt(), h = m.get("h").getAsInt();
				long handle = minecraft.getWindow().handle();
				SDLVideo.SDL_RestoreWindow(handle);
				minecraft.getWindow().setWindowed(w, h);
				SDLVideo.SDL_SetWindowSize(handle, w, h);
				SDLVideo.SDL_SyncWindow(handle);
			}
			default -> {
			}
		}
	}
}
