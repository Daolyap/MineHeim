package dev.daolyap.mineheim.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.mojang.blaze3d.platform.NativeImage;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.client.mixin.AbstractContainerScreenAccessor;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import net.fabricmc.fabric.api.client.screen.v1.ScreenEvents;
import net.fabricmc.fabric.api.client.screen.v1.ScreenMouseEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.gui.screens.inventory.CreativeModeInventoryScreen;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.component.DataComponents;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.Prediction;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.CustomData;

/**
 * The Viking's bag inside Minecraft's inventory screen, so Steve's things and the Viking's are one inventory:
 * <ul>
 * <li>The host sends the bag whenever it changes ({"t":"vinv","cols":8,"rows":4,"items":[[x,y,prefab,name,stack,quality,equipped,twin],...]})
 * and each item's icon once ({"t":"vicon","prefab":..,"png":base64}).</li>
 * <li>Click a Valheim item: it goes on Steve's hotbar as a stand-in (a link, not a copy: the item stays in the bag). With a
 * stand-in selected the host equips the real item and takes the mouse: the hammer builds, a Valheim weapon swings.</li>
 * <li>Shift-click a Valheim item that has a Minecraft twin: it moves into Minecraft ({"t":"vcross"}, the host throws it
 * across as when the Viking drops it).</li>
 * <li>Put a Minecraft item down on the bag: it moves into Valheim (thrown across, as Q throws it). A stand-in put down on
 * the bag is just taken off the hotbar.</li>
 * </ul>
 */
public final class ValheimBag {
	/** Custom data key of a stand-in: the Valheim prefab it stands for. */
	public static final String TAG = "mineheim_valheim";
	private static final int CELL = 18;
	private static final String OPEN = "[open]";
	private static final int OPEN_WIDTH = 30;

	/** One item in the Viking's bag. */
	public record Cell(int x, int y, String prefab, String name, int stack, int quality, boolean equipped, String twin) {
	}

	private static volatile List<Cell> cells = List.of();
	private static volatile int cols = 8, rows = 4;
	private static final Map<String, Identifier> ICONS = new ConcurrentHashMap<>();

	private ValheimBag() {
	}

	// ------------------------------------------------------------------ the host's messages

	/** {"t":"vinv",...} (any thread). */
	static void onInventory(final JsonObject m) {
		List<Cell> list = new ArrayList<>();
		for (JsonElement e : m.getAsJsonArray("items")) {
			JsonArray a = e.getAsJsonArray();
			list.add(new Cell(a.get(0).getAsInt(), a.get(1).getAsInt(), a.get(2).getAsString(), a.get(3).getAsString(), a.get(4).getAsInt(), a.get(5).getAsInt(),
				a.get(6).getAsInt() != 0, a.get(7).getAsString()));
		}

		cols = m.has("cols") ? m.get("cols").getAsInt() : 8;
		rows = m.has("rows") ? m.get("rows").getAsInt() : 4;
		if (list.size() != cells.size()) {
			MineHeim.LOG.info("the Viking's bag: {} item(s), {}x{}", list.size(), cols, rows);
		}

		cells = list;
	}

	/** {"t":"vicon","prefab":..,"png":base64}: registered as a texture (render thread). */
	static void onIcon(final JsonObject m) {
		String prefab = m.get("prefab").getAsString();
		byte[] png = Base64.getDecoder().decode(m.get("png").getAsString());
		Minecraft minecraft = Minecraft.getInstance();
		minecraft.execute(() -> {
			try {
				NativeImage image = NativeImage.read(png);
				Identifier id = Identifier.fromNamespaceAndPath("mineheim", "vicon/" + prefab.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9_./-]", "_"));
				minecraft.getTextureManager().register(id, new DynamicTexture(() -> "MineHeim icon " + prefab, image));
				ICONS.put(prefab, id);
			} catch (IOException | RuntimeException e) {
				MineHeim.LOG.warn("Valheim icon {}: {}", prefab, e.toString());
			}
		});
	}

	// ------------------------------------------------------------------ stand-ins

	/** The Valheim prefab a stand-in stands for, or "" for any other stack. */
	public static String standIn(final ItemStack stack) {
		if (stack == null || stack.isEmpty() || !stack.is(Items.STRUCTURE_VOID)) {
			return "";
		}

		CustomData data = stack.get(DataComponents.CUSTOM_DATA);
		return data == null ? "" : data.copyTag().getStringOr(TAG, "");
	}

	static ItemStack makeStandIn(final String prefab, final String name) {
		ItemStack stack = new ItemStack(Items.STRUCTURE_VOID);
		CompoundTag tag = new CompoundTag();
		tag.putString(TAG, prefab);
		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		stack.set(DataComponents.CUSTOM_NAME, Component.literal(name + " (Valheim)"));
		stack.set(DataComponents.MAX_STACK_SIZE, 1);
		return stack;
	}

	/** A stand-in is drawn with its Valheim icon (true: drawn, Minecraft's own drawing skipped). */
	public static boolean drawStandIn(final GuiGraphicsExtractor g, final ItemStack stack, final int x, final int y) {
		String prefab = standIn(stack);
		if (prefab.isEmpty()) {
			return false;
		}

		icon(g, prefab, x, y);
		if (cells.stream().noneMatch(c -> c.prefab().equals(prefab))) {
			g.fill(x, y, x + 16, y + 16, 0x80400000); // the Viking no longer has it
		}

		return true;
	}

	private static void icon(final GuiGraphicsExtractor g, final String prefab, final int x, final int y) {
		Identifier id = ICONS.get(prefab);
		if (id != null) {
			g.blit(RenderPipelines.GUI_TEXTURED, id, x, y, 0.0F, 0.0F, 16, 16, 64, 64, 64, 64);
		} else {
			g.fill(x + 1, y + 1, x + 15, y + 15, 0xFF5A4A32);
			g.text(Minecraft.getInstance().font, prefab.isEmpty() ? "?" : prefab.substring(0, 1), x + 5, y + 4, 0xFFE8D8B0, true);
		}
	}

	// ------------------------------------------------------------------ the panel in Minecraft's inventory screen

	/** Client init: the bag goes next to the player's inventory (Survival's and Creative's). */
	static void register() {
		ScreenEvents.AFTER_INIT.register((client, screen, width, height) -> {
			if (!(screen instanceof InventoryScreen) && !(screen instanceof CreativeModeInventoryScreen)) {
				return;
			}

			ScreenEvents.afterExtract(screen).register((s, g, mouseX, mouseY, delta) -> {
				if (MineHeim.active) {
					draw(s, g, mouseX, mouseY);
				}
			});
			ScreenMouseEvents.allowMouseClick(screen).register((s, event) -> !MineHeim.active || !click(s, event));
		});
	}

	/**
	 * Where the panel goes: beside the inventory on the side with more room, scaled down when it does not fit there (at a
	 * large GUI scale the Creative inventory leaves only a narrow strip either side). {x, y, scale}; the panel is drawn in
	 * its own coordinates (w x h, see {@link #size}) from there.
	 */
	private static float[] origin(final Screen screen) {
		AbstractContainerScreenAccessor a = (AbstractContainerScreenAccessor) screen;
		int[] size = size();
		int left = a.mineheim$leftPos(), right = screen.width - (left + a.mineheim$imageWidth());
		int room = Math.max(left, right) - 6;
		float scale = Math.max(0.4F, Math.min(1.0F, room / (float) size[0]));
		float x = right >= left ? left + a.mineheim$imageWidth() + 3 : left - 3 - size[0] * scale;
		return new float[] {Math.max(0, x), a.mineheim$topPos(), scale};
	}

	/** The panel's own size, unscaled: the grid, its title and two lines of hint under it. */
	private static int[] size() {
		return new int[] {Math.max(cols * CELL + 8, 120), rows * CELL + 18};
	}

	/** Screen coordinates to the panel's own. */
	private static double[] local(final float[] o, final double x, final double y) {
		return new double[] {(x - o[0]) / o[2], (y - o[1]) / o[2]};
	}

	private static void draw(final Screen screen, final GuiGraphicsExtractor g, final int screenX, final int screenY) {
		float[] o = origin(screen);
		double[] m = local(o, screenX, screenY);
		int mouseX = (int) Math.floor(m[0]), mouseY = (int) Math.floor(m[1]);
		int[] size = size();
		g.pose().pushMatrix();
		g.pose().translate(o[0], o[1]);
		g.pose().scale(o[2], o[2]);
		try {
			drawPanel(g, size[0], size[1], mouseX, mouseY, screen instanceof CreativeModeInventoryScreen);
		} finally {
			g.pose().popMatrix();
		}
	}

	private static void drawPanel(final GuiGraphicsExtractor g, final int w, final int h, final int mouseX, final int mouseY, final boolean creative) {
		int x0 = 0, y0 = 0;
		int[] o = {0, 0, w, h};
		g.fill(x0, y0, x0 + o[2], y0 + o[3], 0xE0201810);
		g.outline(x0, y0, o[2], o[3], 0xFF8A7048);
		g.text(Minecraft.getInstance().font, "Viking's bag", x0 + 4, y0 + 4, 0xFFE8D8B0, false);
		// the Viking's own inventory (armour, crafting, sorting): opens Valheim's screen instead of this one
		boolean overOpen = mouseX >= x0 + o[2] - OPEN_WIDTH && mouseX < x0 + o[2] && mouseY >= y0 && mouseY < y0 + 13;
		g.text(Minecraft.getInstance().font, OPEN, x0 + o[2] - OPEN_WIDTH, y0 + 4, overOpen ? 0xFFFFFFA0 : 0xFFB0A080, false);
		Cell hovered = null;
		for (int r = 0; r < rows; r++) {
			for (int c = 0; c < cols; c++) {
				int x = x0 + 4 + c * CELL, y = y0 + 14 + r * CELL;
				g.fill(x, y, x + 16, y + 16, 0xFF3A3024);
			}
		}

		for (Cell cell : cells) {
			int x = x0 + 4 + cell.x() * CELL, y = y0 + 14 + cell.y() * CELL;
			icon(g, cell.prefab(), x, y);
			if (cell.stack() > 1) {
				String n = String.valueOf(cell.stack());
				g.text(Minecraft.getInstance().font, n, x + 17 - Minecraft.getInstance().font.width(n), y + 9, 0xFFFFFFFF, true);
			}

			if (cell.equipped()) {
				g.outline(x - 1, y - 1, 18, 18, 0xFF60C060);
			}

			if (mouseX >= x && mouseX < x + 16 && mouseY >= y && mouseY < y + 16) {
				hovered = cell;
				g.fill(x, y, x + 16, y + 16, 0x40FFFFFF);
			}
		}

		if (hovered == null && creative) {
			g.text(Minecraft.getInstance().font, "click: onto the hotbar", x0 + 4, y0 + o[3] + 2, 0xFFB0A080, true);
			g.text(Minecraft.getInstance().font, "Creative: nothing crosses", x0 + 4, y0 + o[3] + 12, 0xFFB0A080, true);
		}

		if (hovered != null) {
			String hint = hovered.twin().isEmpty() ? "click: onto the hotbar" : "click: onto the hotbar, shift-click: into Minecraft";
			g.text(Minecraft.getInstance().font, hovered.name() + (hovered.quality() > 1 ? " *" + hovered.quality() : ""), x0 + 4, y0 + o[3] + 2, 0xFFFFFFFF, true);
			g.text(Minecraft.getInstance().font, hint, x0 + 4, y0 + o[3] + 12, 0xFFB0A080, true);
		}
	}

	/** The bag took the last button press: its release is the bag's too. */
	private static boolean ownsRelease;

	/**
	 * A button the host forwarded (ClientInput calls the screen straight, so Fabric's mouse events, fired in Minecraft's
	 * MouseHandler, never see it): true when it was the bag's, and the screen must not see it.
	 */
	static boolean hostButton(final Screen screen, final MouseButtonEvent event, final boolean down) {
		if (!MineHeim.active || !(screen instanceof InventoryScreen) && !(screen instanceof CreativeModeInventoryScreen)) {
			return false;
		}

		if (!down) {
			boolean owned = ownsRelease;
			ownsRelease = false;
			return owned;
		}

		ownsRelease = click(screen, event);
		return ownsRelease;
	}

	/** A click on the panel (true: it was the panel's, Minecraft's screen does not see it). */
	private static boolean click(final Screen screen, final MouseButtonEvent event) {
		double[] m = local(origin(screen), event.x(), event.y());
		int[] size = size();
		int[] o = {0, 0, size[0], size[1]};
		double mx = m[0], my = m[1];
		if (mx < o[0] || my < o[1] || mx >= o[0] + o[2] || my >= o[1] + o[3]) {
			return false;
		}

		Minecraft minecraft = Minecraft.getInstance();
		if (mx >= o[0] + o[2] - OPEN_WIDTH && my < o[1] + 13) {
			minecraft.gui.setScreen(null);
			MineHeim.events.accept("{\"t\":\"vopen\"}");
			return true;
		}

		AbstractContainerScreen<?> container = (AbstractContainerScreen<?>) screen;
		ItemStack carried = container.getMenu().getCarried();
		if (!carried.isEmpty()) {
			MineHeim.LOG.info("the Viking's bag: put down {}", carried);
			putDown(minecraft, screen instanceof CreativeModeInventoryScreen);
			return true;
		}

		int c = (int) Math.floor((mx - o[0] - 4) / CELL), r = (int) Math.floor((my - o[1] - 14) / CELL);
		Cell cell = cells.stream().filter(x -> x.x() == c && x.y() == r).findFirst().orElse(null);
		if (cell == null) {
			return true;
		}

		MineHeim.LOG.info("the Viking's bag: {} {} ({})", event.hasShiftDown() && !cell.twin().isEmpty() ? "into Minecraft:" : "onto the hotbar:", cell.prefab(), cell.name());
		if (event.hasShiftDown() && !cell.twin().isEmpty()) {
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"vcross\",\"x\":%d,\"y\":%d}", cell.x(), cell.y()));
		} else {
			toHotbar(minecraft, cell);
		}

		return true;
	}

	/**
	 * A stand-in for `cell` in the selected hotbar slot, so it is in hand at once (the hammer builds as soon as the screen
	 * closes). What was in that slot moves into the inventory; with no room for it, nothing changes.
	 */
	private static void toHotbar(final Minecraft minecraft, final Cell cell) {
		if (minecraft.player == null) {
			return;
		}

		int slot = minecraft.player.getInventory().getSelectedSlot();
		onServer(p -> {
			Inventory inv = p.getInventory();
			for (int i = 0; i < Inventory.SELECTION_SIZE; i++) {
				if (i != slot && standIn(inv.getItem(i)).equals(cell.prefab())) {
					inv.setItem(i, ItemStack.EMPTY); // one stand-in per item
				}
			}

			ItemStack there = inv.getItem(slot);
			if (!there.isEmpty() && standIn(there).isEmpty()) {
				int free = inv.getFreeSlot();
				if (free < 0) {
					p.sendOverlayMessage(Component.literal("No room in the inventory for " + there.getHoverName().getString()));
					return;
				}

				inv.setItem(free, there);
			}

			inv.setItem(slot, makeStandIn(cell.prefab(), cell.name()));
			p.inventoryMenu.broadcastChanges();
		});
	}

	/** The carried stack put down on the bag: a stand-in is removed, a Minecraft item is thrown across into Valheim. */
	private static void putDown(final Minecraft minecraft, final boolean creative) {
		onServer(p -> {
			ItemStack carried = p.containerMenu.getCarried();
			if (carried.isEmpty()) {
				return;
			}

			p.containerMenu.setCarried(ItemStack.EMPTY);
			if (standIn(carried).isEmpty()) {
				p.drop(carried, false, Prediction.SERVER_ONLY); // offered to the host like a thrown item (HostItems)
			}

			p.containerMenu.broadcastChanges();
		});
		if (creative && minecraft.player != null) {
			minecraft.player.sendOverlayMessage(Component.literal("Creative items stay in Minecraft (Survival lets them cross)"));
		}
	}

	private static void onServer(final java.util.function.Consumer<ServerPlayer> action) {
		MinecraftServer s = Minecraft.getInstance().getSingleplayerServer();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			if (!s.getPlayerList().getPlayers().isEmpty()) {
				action.accept(s.getPlayerList().getPlayers().get(0));
			}
		});
	}
}
