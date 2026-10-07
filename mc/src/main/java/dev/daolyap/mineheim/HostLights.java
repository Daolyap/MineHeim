package dev.daolyap.mineheim;

import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LightBlock;
import net.minecraft.world.level.block.state.BlockState;

/**
 * Light shared with the host: its fires as light blocks here, the time of day, and this world's lamps (torches,
 * lanterns, glowstone, lava, ...) reported to the host so it can light its own world around them.
 */
public final class HostLights {
	/** Light blocks placed for the host's fires: the cell the host asked for -> where the light went (the first free cell up). */
	private static final Map<BlockPos, BlockPos> fires = new ConcurrentHashMap<>();
	/** Light-giving blocks the host knows about (server thread only). */
	private static final Set<BlockPos> lamps = new HashSet<>();
	/** Lamp changes this tick: level, 0 = gone (server thread only). */
	private static final Map<BlockPos, Integer> changes = new LinkedHashMap<>();

	private HostLights() {
	}

	static void detach() {
		fires.clear();
		lamps.clear();
		changes.clear();
	}

	/** {"t":"vlights","c":[x,y,z,level,...],"r":[x,y,z,...]} */
	public static void fires(final int[] add, final int[] remove) {
		MinecraftServer s = WorldBridge.server();
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			WorldBridge.placingGround = true;
			for (int i = 0; i + 2 < remove.length; i += 3) {
				BlockPos asked = new BlockPos(remove[i], remove[i + 1], remove[i + 2]);
				BlockPos at = fires.remove(asked);
				at = at != null ? at : asked;
				if (level.isInWorldBounds(at) && level.getBlockState(at).is(Blocks.LIGHT)) {
					level.setBlock(at, Blocks.AIR.defaultBlockState(), Block.UPDATE_ALL);
				}
			}

			for (int i = 0; i + 3 < add.length; i += 4) {
				BlockPos asked = new BlockPos(add[i], add[i + 1], add[i + 2]);
				BlockState light = Blocks.LIGHT.defaultBlockState().setValue(LightBlock.LEVEL, Math.max(1, Math.min(15, add[i + 3])));
				BlockPos at = fires.get(asked);
				if (at == null || !level.getBlockState(at).is(Blocks.LIGHT)) {
					at = null;
					// the flame's own cell may be a barrier (the fire's building piece): the first free cell above it
					for (int up = 0; up <= 2 && at == null; up++) {
						BlockPos p = asked.above(up);
						if (level.isInWorldBounds(p) && level.getBlockState(p).isAir()) {
							at = p;
						}
					}
				}

				if (at != null) {
					level.setBlock(at, light, Block.UPDATE_ALL);
					fires.put(asked, at);
				}
			}

			WorldBridge.placingGround = false;
		});
	}

	/** The host's time of day in ticks (0 sunrise, 6000 noon, 18000 midnight); Minecraft's own clock is stopped. */
	public static void dayTime(final int ticks) {
		MinecraftServer s = WorldBridge.server();
		if (s != null) {
			s.execute(() -> s.getCommands().performPrefixedCommand(s.createCommandSourceStack().withSuppressedOutput(), "time set " + ticks));
		}
	}

	/** Server thread, from WorldBridge.onBlockChanged: a lamp appeared or went (our own fire lights are not lamps). */
	static void onBlockChanged(final BlockPos pos, final BlockState state) {
		int light = state.is(Blocks.LIGHT) ? 0 : state.getLightEmission();
		if (light > 0) {
			BlockPos p = pos.immutable();
			lamps.add(p);
			changes.put(p, light);
		} else if (lamps.remove(pos)) {
			changes.put(pos.immutable(), 0);
		}
	}

	/** From WorldBridge.sync: a lamp found near the player. */
	static void found(final BlockPos pos, final BlockState state) {
		if (!state.is(Blocks.LIGHT) && state.getLightEmission() > 0) {
			BlockPos p = pos.immutable();
			lamps.add(p);
			changes.put(p, state.getLightEmission());
		}
	}

	/** End of each server tick: {"t":"mclights","set":[x,y,z,level,...],"clear":[x,y,z,...]}. */
	static void flush() {
		if (changes.isEmpty()) {
			return;
		}

		StringBuilder set = new StringBuilder();
		StringBuilder clear = new StringBuilder();
		for (Map.Entry<BlockPos, Integer> e : changes.entrySet()) {
			BlockPos p = e.getKey();
			if (e.getValue() > 0) {
				set.append(set.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ()).append(',').append(e.getValue());
			} else {
				clear.append(clear.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ());
			}
		}

		changes.clear();
		MineHeim.events.accept("{\"t\":\"mclights\",\"set\":[" + set + "],\"clear\":[" + clear + "]}");
	}

	static int fireCount() {
		return fires.size();
	}
}
