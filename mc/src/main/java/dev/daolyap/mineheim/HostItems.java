package dev.daolyap.mineheim;

import java.util.Locale;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/**
 * Items crossing between this world and the host's, moved and never copied (the host's ItemBridge holds the table of
 * which item is which):
 * <ul>
 * <li>the host's items arrive as {"t":"itemin"} and land here as item entities (already gone on the host's side)</li>
 * <li>an item the player throws is offered to the host once ({"t":"itemout"}); if the host asks for it ({"t":"itemtake"})
 * that part of the stack is taken away here, and only then confirmed ({"t":"itemtaken"}) for the host to create</li>
 * </ul>
 */
public final class HostItems {
	/** Offered once (or arrived from the host): never offered again while it lies there. Picked up and thrown again, it is a new entity. */
	private static final String OFFERED_TAG = "mineheim_offered";

	private HostItems() {
	}

	/** A stand-in for a Valheim item on Steve's hotbar (the client's ValheimBag makes them). */
	static boolean isStandIn(final ItemStack stack) {
		CustomData data = stack.is(Items.STRUCTURE_VOID) ? stack.get(DataComponents.CUSTOM_DATA) : null;
		return data != null && data.copyTag().contains("mineheim_valheim");
	}

	private static String key(final ItemStack stack) {
		return BuiltInRegistries.ITEM.getKey(stack.getItem()).toString();
	}

	/** Every server tick: offer the player's freshly thrown items (a few ticks after the throw, so the host sees where they fly). */
	static void tick(final ServerLevel level) {
		for (Entity e : level.getAllEntities()) {
			if (!(e instanceof ItemEntity item) || item.getAge() < 4 || !(item.getOwner() instanceof Player) || item.entityTags().contains(OFFERED_TAG)) {
				continue;
			}

			item.addTag(OFFERED_TAG);
			ItemStack stack = item.getItem();
			if (stack.isEmpty()) {
				continue;
			}

			if (isStandIn(stack)) {
				item.discard(); // a Valheim item's stand-in is only a link: thrown away, it is gone (the item stays in the Viking's bag)
				continue;
			}

			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"itemout\",\"id\":%d,\"item\":\"%s\",\"n\":%d,\"pos\":[%.3f,%.3f,%.3f]}",
				e.getId(), key(stack), stack.getCount(), e.getX(), e.getY(), e.getZ()));
		}
	}

	/** {"t":"itemin","item":"minecraft:oak_log","n":5,"pos":[x,y,z],"vel":[x,y,z]}: the host's items, thrown into this world. */
	public static void arrive(final String id, final int count, final double x, final double y, final double z, final double vx, final double vy, final double vz) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			Identifier key = Identifier.tryParse(id);
			Item item = key == null ? Items.AIR : BuiltInRegistries.ITEM.getValue(key);
			if (item == Items.AIR) {
				MineHeim.LOG.warn("itemin: no item {} in this Minecraft ({} lost)", id, count);
				return;
			}

			ServerLevel level = s.overworld();
			int left = count;
			while (left > 0) {
				ItemStack stack = new ItemStack(item, 1);
				stack.setCount(Math.min(left, stack.getMaxStackSize()));
				left -= stack.getCount();
				ItemEntity e = new ItemEntity(level, x, y, z, stack, vx, vy, vz);
				e.setPickUpDelay(10);
				e.addTag(OFFERED_TAG); // it came from the host: not offered straight back
				level.addFreshEntity(e);
			}

			MineHeim.LOG.info("itemin: {} {} at {} {} {}", count, id, String.format(Locale.ROOT, "%.1f", x), String.format(Locale.ROOT, "%.1f", y), String.format(Locale.ROOT, "%.1f", z));
		});
	}

	/** {"t":"itemtake","id":entity,"n":count}: the host wants `count` of a thrown stack. Only if it is still lying there. */
	public static void take(final int id, final int count) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			Entity e = s.overworld().getEntity(id);
			if (!(e instanceof ItemEntity item) || !item.isAlive() || count <= 0 || item.getItem().getCount() < count) {
				return; // picked up again, merged or despawned: nothing crosses
			}

			ItemStack stack = item.getItem().copy();
			String key = key(stack);
			stack.shrink(count);
			if (stack.isEmpty()) {
				item.discard();
			} else {
				item.setItem(stack);
			}

			MineHeim.LOG.info("itemtaken: {} {} crossed to the host", count, key);
			MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"itemtaken\",\"id\":%d,\"item\":\"%s\",\"n\":%d,\"pos\":[%.3f,%.3f,%.3f]}",
				id, key, count, item.getX(), item.getY(), item.getZ()));
		});
	}

	/** {"t":"query"}: for tests and diagnosis, the clock, the loose items near the player and what the player carries. */
	public static void query() {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			ServerPlayer player = s.getPlayerList().getPlayers().isEmpty() ? null : s.getPlayerList().getPlayers().get(0);
			StringBuilder b = new StringBuilder(String.format(Locale.ROOT, "{\"t\":\"query\",\"time\":%d,\"darken\":%d,\"fires\":%d,\"items\":[",
				level.getOverworldClockTime() % 24000L, level.getSkyDarken(), HostLights.fireCount()));
			boolean first = true;
			for (Entity e : level.getAllEntities()) {
				if (e instanceof ItemEntity item && (player == null || e.distanceTo(player) < 24f)) {
					b.append(first ? "" : ",").append(String.format(Locale.ROOT, "[\"%s\",%d,%.2f,%.2f,%.2f]", key(item.getItem()), item.getItem().getCount(), e.getX(), e.getY(), e.getZ()));
					first = false;
				}
			}

			if (player != null) {
				b.append(String.format(Locale.ROOT, "],\"hp\":%.2f,\"maxHp\":%.1f,\"food\":%d", player.getHealth(), player.getMaxHealth(), player.getFoodData().getFoodLevel()));
			} else {
				b.append(']');
			}

			b.append(",\"inv\":[");
			if (player != null) {
				Inventory inv = player.getInventory();
				first = true;
				for (int i = 0; i < inv.getContainerSize(); i++) {
					ItemStack st = inv.getItem(i);
					if (!st.isEmpty()) {
						b.append(first ? "" : ",").append(String.format(Locale.ROOT, "[\"%s\",%d]", key(st), st.getCount()));
						first = false;
					}
				}
			}

			MineHeim.events.accept(b.append("]}").toString());
		});
	}
}
