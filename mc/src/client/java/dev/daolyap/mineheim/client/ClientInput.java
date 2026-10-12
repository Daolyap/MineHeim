package dev.daolyap.mineheim.client;

import com.google.gson.JsonObject;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.WorldBridge;
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
import net.minecraft.sounds.SoundSource;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import org.lwjgl.sdl.SDLKeyboard;
import org.lwjgl.sdl.SDLVideo;

/** Host input, applied on the client thread: the host window has the focus, so Minecraft never sees these itself. */
public final class ClientInput {
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

	/** What the crosshair is on, if it is one of Minecraft's own blocks (the host's barrier ground doesn't count). */
	private static BlockHitResult realBlock(final Minecraft minecraft) {
		HitResult hit = minecraft.hitResult;
		if (minecraft.level == null || !(hit instanceof BlockHitResult block) || hit.getType() != HitResult.Type.BLOCK) {
			return null;
		}

		return minecraft.level.getBlockState(block.getBlockPos()).is(Blocks.BARRIER) ? null : block;
	}

	/**
	 * Minecraft is swinging (Minecraft.startAttack, before it resets the recharge): at no block of its own, the host hits
	 * what's in front of Steve in its world (creatures with anything in hand, rocks and trees with a pickaxe or axe), for
	 * what the swing deals here: the held item's attack damage, scaled by the recharge exactly as Minecraft scales it.
	 */
	public static void swing(final Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (player == null || realBlock(minecraft) != null || !ValheimBag.standIn(player.getMainHandItem()).isEmpty()) {
			return; // (a Valheim stand-in in hand: the Viking attacks with the real item in Valheim)
		}

		String item = BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath();
		float charge = player.getAttackStrengthScale(0.5F);
		double damage = attackDamage(player) * (0.2 + 0.8 * charge * charge);
		MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"melee\",\"item\":\"%s\",\"dmg\":%.2f,\"charge\":%.2f,\"ench\":\"%s\"}", item, damage, charge,
			WorldBridge.enchantments(player.getMainHandItem())));
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
				boolean down = m.get("down").getAsBoolean();
				// (a call straight to the screen passes Fabric's mouse events by, which fire in MouseHandler: the bag asks first)
				boolean handled = ValheimBag.hostButton(screen, event, down) || (down ? screen.mouseClicked(event, false) : screen.mouseReleased(event));
				screen.afterMouseAction();
				MineHeim.LOG.info("screen {} button {} {} at {},{}: {}", screen.getClass().getSimpleName(), m.get("b").getAsInt(),
					m.get("down").getAsBoolean() ? "down" : "up", (int) x, (int) y, handled);
			}
			case "mscroll" -> minecraft.mouseHandler.onScroll(window, 0.0, m.get("d").getAsDouble());
			case "kpress" -> {
				// a key event carries the scancode (what key mappings match) and the layout's keycode (what text boxes read:
				// Backspace, Delete, the arrows, Ctrl+A/C/V): SDL gives the second from the first
				int scancode = m.get("k").getAsInt();
				int keycode = SDLKeyboard.SDL_GetKeyFromScancode(scancode, (short) 0, true);
				minecraft.keyboardHandler.keyPress(window, m.get("down").getAsBoolean() ? 1 : 0, new KeyEvent(scancode, keycode, mods(m)));
			}
			case "char" -> minecraft.keyboardHandler.charTyped(window, new CharacterEvent(m.get("c").getAsInt()));
			case "window" -> {
				// the host shows Minecraft's picture: its own window stays out of the way (parked off-screen: it keeps rendering)
				WindowParking.set(window, m.get("hidden").getAsBoolean());
				if (m.has("host")) {
					HostView.watchHost(m.get("host").getAsLong(), !m.has("quitWithHost") || m.get("quitWithHost").getAsBoolean());
				}
			}
			case "quit" -> {
				// the host is closing: save and quit with it
				MineHeim.LOG.info("the host is closing: quitting Minecraft");
				minecraft.stop();
				HostView.quitWatchdog();
			}
			case "audio" -> {
				// Minecraft's window is hidden, so its music and volume are the host's to set (Audio.* in the plugin's config)
				minecraft.options.getSoundSourceOptionInstance(SoundSource.MUSIC).set(Math.clamp(m.get("music").getAsDouble(), 0.0, 1.0));
				minecraft.options.getSoundSourceOptionInstance(SoundSource.MASTER).set(Math.clamp(m.get("master").getAsDouble(), 0.0, 1.0));
			}
			case "steve" -> {
				// Minecraft mode in the host: Steve, his hand and Minecraft's HUD show; off, the host's own player is back
				boolean on = m.get("on").getAsBoolean();
				HostView.steve = on;
				HostView.steveBody = !m.has("body") || m.get("body").getAsBoolean();
				if (minecraft.gui.hud.isHidden() == on) {
					minecraft.gui.hud.toggle();
				}
			}
			case "interact" -> {
				// the host player's "use" on one of Minecraft's chests, crafting tables, furnaces...: open it
				BlockHitResult block = realBlock(minecraft);
				if (player != null && block != null && minecraft.gameMode != null && minecraft.gui.screen() == null
					&& minecraft.level.getBlockState(block.getBlockPos()).getMenuProvider(minecraft.level, block.getBlockPos()) != null) {
					minecraft.gameMode.useItemOn(player, InteractionHand.MAIN_HAND, block);
				}
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
				if (k.equals("pick") && down && realBlock(minecraft) == null) {
					return; // the host's invisible ground and buildings are barriers: nothing to pick there
				}

				// (a swing reaches the host from Minecraft.startAttack, when Minecraft actually swings: see swing())

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
