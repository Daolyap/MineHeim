package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.CreatureProxy;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.WorldBridge;
import java.util.List;
import java.util.Optional;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.client.AttackIndicatorStatus;
import net.minecraft.client.CloudStatus;
import net.minecraft.client.InactivityFpsLimit;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;
import net.minecraft.client.gui.screens.DeathScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.renderer.entity.NoopRenderer;
import net.minecraft.client.tutorial.TutorialSteps;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.HolderSet;
import net.minecraft.core.registries.Registries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.biome.Biomes;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.FlatLevelSource;
import net.minecraft.world.level.levelgen.WorldDimensions;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.flat.FlatLayerInfo;
import net.minecraft.world.level.levelgen.flat.FlatLevelGeneratorSettings;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

public class MineHeimClient implements ClientModInitializer {
	/** The world the host plays in: an empty (void) creative world, so only what the player builds is Minecraft. */
	private static final String WORLD = "mineheim";
	/** Run on the server once the player has joined (both game modes). */
	private static final List<String> RULES = List.of(
		"gamerule advance_time false",
		"gamerule advance_weather false",
		"gamerule spawn_mobs false",
		"gamerule spawn_monsters false",
		"gamerule spawn_patrols false",
		"gamerule spawn_phantoms false",
		"gamerule spawn_wandering_traders false",
		"gamerule send_command_feedback false",
		"gamerule log_admin_commands false",
		"gamerule keep_inventory true",
		"difficulty normal",
		"gamerule show_advancement_messages false",
		"gamerule player_movement_check false",
		"time set noon",
		"weather clear"
	);
	/** Creative only: the builder's kit (Survival keeps what the player has). The shield sits in the off hand, raised with the right button. */
	private static final List<String> KIT = List.of(
		"clear @a",
		"item replace entity @a hotbar.0 with minecraft:diamond_pickaxe",
		"item replace entity @a hotbar.1 with minecraft:diamond_sword",
		"item replace entity @a hotbar.2 with minecraft:bow[enchantments={power:5,infinity:1}]",
		"item replace entity @a hotbar.3 with minecraft:tnt 64",
		"item replace entity @a hotbar.4 with minecraft:flint_and_steel",
		"item replace entity @a hotbar.5 with minecraft:creeper_spawn_egg 64",
		"item replace entity @a hotbar.6 with minecraft:cobblestone 64",
		"item replace entity @a hotbar.7 with minecraft:oak_planks 64",
		"item replace entity @a hotbar.8 with minecraft:diamond_axe",
		"item replace entity @a weapon.offhand with minecraft:shield",
		"give @a minecraft:arrow 64"
	);
	/** "creative" or "survival": the host's choice ({"t":"gamemode","m":...}); the host owns health either way. */
	private static volatile String gameMode = "creative";
	/** The shield state last reported to the host ({"t":"shield","up":bool}). */
	private static boolean reportedShield;
	/** The Minecraft screen last reported to the host ("" = none). */
	private static String reportedScreen = "";
	private static boolean configured;
	private static boolean worldRequested;
	/** Server ticks until the setup commands run (the player isn't in the player list yet when JOIN fires). */
	private static int setupIn = -1;
	private static int respawnIn;
	/** The hotbar slot last reported to the host ({"t":"selected","n":slot}), so it can see its number keys and wheel arrive. */
	private static int reportedSlot = -1;

	@Override
	public void onInitializeClient() {
		HostLink.launch();
		EntityRendererRegistry.register(CreatureProxy.TYPE, NoopRenderer::new); // the host's creatures are drawn by the host
		ClientTickEvents.END_CLIENT_TICK.register(MineHeimClient::tick);
		// however Minecraft is closed, it must not linger in Task Manager: a watchdog ends the process if the quit stalls
		ClientLifecycleEvents.CLIENT_STOPPING.register(client -> HostView.onClientStopping());
		ServerLifecycleEvents.SERVER_STOPPED.register(server -> HostView.onServerStopped());
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> {
			ServerPlayer player = handler.player;
			// never left gliding from a previous session: with no host yet it would glide down into the void
			player.stopFallFlying();
			player.setItemSlot(EquipmentSlot.CHEST, ItemStack.EMPTY);
			player.getAbilities().flying = true;
			player.onUpdateAbilities();
			setupIn = 10;
		});
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			if (setupIn > 0 && --setupIn == 0) {
				RULES.forEach(WorldBridge::command);
				applyGameMode();
			}

			// the host moves the player: it must always be able to fly, or Survival's gravity and fall damage would fight the host
			if (MineHeim.active) {
				for (ServerPlayer p : server.getPlayerList().getPlayers()) {
					// Creative is Steve's: while the host's player is the Viking (Minecraft mode off) he plays Survival, so
					// Minecraft's mobs see him, hunt him and fight back when he hits them (no mob ever targets a Creative player)
					GameType wanted = gameMode.equals("creative") && HostView.steve ? GameType.CREATIVE : GameType.SURVIVAL;
					if (setupIn <= 0 && p.gameMode() != wanted) {
						p.setGameMode(wanted);
					}

					if (!p.getAbilities().mayfly || !p.getAbilities().flying) {
						p.getAbilities().mayfly = true;
						p.getAbilities().flying = true;
						p.onUpdateAbilities();
					}

					// Survival's hearts and hunger show the host's player (never 0: Minecraft must not see a death of its own)
					if (vikingHealth >= 0F && p.gameMode().isSurvival()) {
						float health = Math.max(0.5F, vikingHealth * p.getMaxHealth());
						if (Math.abs(p.getHealth() - health) > 0.01F) {
							p.setHealth(health);
						}

						if (p.getFoodData().getFoodLevel() != vikingFood) {
							p.getFoodData().setFoodLevel(vikingFood);
						}
					}
				}
			}
		});
	}

	/** The host's player's health (share of its max, -1 = not known yet) and food (0-20), shown by Survival's hearts and hunger. */
	private static volatile float vikingHealth = -1F;
	private static volatile int vikingFood = 20;

	static void vikingStats(final float health, final int food) {
		vikingHealth = Math.max(0F, Math.min(1F, health));
		vikingFood = Math.max(0, Math.min(20, food));
	}

	/** The host chose a game mode (sent on every connect and whenever its config changes). */
	static void setGameMode(final String mode) {
		String next = "survival".equals(mode) ? "survival" : "creative";
		if (next.equals(gameMode)) {
			return; // the host repeats it on every reconnect: don't hand out a fresh kit each time
		}

		gameMode = next;
		MineHeim.LOG.info("game mode -> {}", next);
		applyGameMode();
	}

	/** Runs on the server thread through WorldBridge.command; harmless before a world exists (the join setup applies it then). */
	private static void applyGameMode() {
		WorldBridge.command("gamemode " + gameMode + " @a");
		if (gameMode.equals("creative")) {
			KIT.forEach(WorldBridge::command);
		}
	}

	private static void tick(final Minecraft minecraft) {
		// a death (the void) would leave the death screen over the host's picture: respawn straight away
		if (minecraft.player != null && minecraft.gui.screen() instanceof DeathScreen && --respawnIn <= 0) {
			respawnIn = 40;
			minecraft.player.respawn();
		}

		// a Minecraft screen opened or closed (inventory, chat, a chest...): the host hands it the cursor and keys meanwhile
		Screen screen = minecraft.gui.screen();
		String screenName = minecraft.level != null && screen != null && !(screen instanceof DeathScreen) ? screen.getClass().getSimpleName() : "";
		if (!screenName.equals(reportedScreen)) {
			reportedScreen = screenName;
			MineHeim.events.accept("{\"t\":\"screen\",\"open\":" + !screenName.isEmpty() + ",\"name\":\"" + screenName + "\"}");
		}

		if (minecraft.player != null) {
			// a raised shield (held right button with a shield in either hand) blocks on the host's side as well
			boolean shield = minecraft.player.isBlocking();
			if (shield != reportedShield) {
				reportedShield = shield;
				MineHeim.events.accept("{\"t\":\"shield\",\"up\":" + shield + "}");
			}

			int slot = minecraft.player.getInventory().getSelectedSlot();
			if (slot != reportedSlot) {
				reportedSlot = slot;
				MineHeim.events.accept("{\"t\":\"selected\",\"n\":" + slot + "}");
			}
		}

		NetherAir.tick(minecraft);
		if (!configured) {
			configured = true;
			configure(minecraft.options);
		}

		if (!worldRequested && minecraft.level == null && minecraft.gui.screen() instanceof TitleScreen && !Boolean.getBoolean("mineheim.noAutoWorld")) {
			worldRequested = true;
			openWorld(minecraft);
		}
	}

	/** Settings for sitting behind another game: keep running unfocused, and no sky/cloud/bobbing effects in the picture. */
	private static void configure(final Options options) {
		options.pauseOnLostFocus = false;
		options.onboardAccessibility = false;
		options.tutorialStep = TutorialSteps.NONE;
		options.cloudStatus().set(CloudStatus.OFF);
		options.bobView().set(false);
		options.vignette().set(false);
		options.improvedTransparency().set(false);
		options.inactivityFpsLimit().set(InactivityFpsLimit.MINIMIZED);
		options.fovEffectScale().set(0.0);
		options.damageTiltStrength().set(0.0);
		options.menuBackgroundBlurriness().set(0);
		options.enableVsync().set(false);
		// the crosshair (and its attack-recharge mark) is the host's: show the recharge next to the hotbar instead
		options.attackIndicator().set(AttackIndicatorStatus.HOTBAR);
		// the host shows ~60-120 fps: rendering faster only competes with it for the GPU
		options.framerateLimit().set(120);
		// no music until the host says otherwise (Audio.MinecraftMusic): there is no window to turn it down in
		options.getSoundSourceOptionInstance(SoundSource.MUSIC).set(0.0);
		options.save();
	}

	private static void openWorld(final Minecraft minecraft) {
		if (minecraft.getLevelSource().levelExists(WORLD)) {
			MineHeim.LOG.info("opening world {}", WORLD);
			minecraft.createWorldOpenFlows().openWorld(WORLD, () -> minecraft.gui.setScreen(new TitleScreen()));
		} else {
			MineHeim.LOG.info("creating world {}", WORLD);
			LevelSettings settings = new LevelSettings("MineHeim", GameType.CREATIVE, LevelSettings.DifficultySettings.DEFAULT, true, WorldDataConfiguration.DEFAULT);
			minecraft.createWorldOpenFlows().createFreshLevel(WORLD, settings, new WorldOptions(0L, false, false), MineHeimClient::voidWorld, minecraft.gui.screen());
		}
	}

	/** A flat world with a single layer of air: nothing but what gets built (the host's ground arrives as barriers). */
	private static WorldDimensions voidWorld(final HolderLookup.Provider registries) {
		FlatLevelGeneratorSettings flat = new FlatLevelGeneratorSettings(
			Optional.of(HolderSet.direct()), registries.lookupOrThrow(Registries.BIOME).getOrThrow(Biomes.PLAINS), List.of()
		);
		flat.getLayersInfo().add(new FlatLayerInfo(1, Blocks.AIR));
		flat.updateLayers();
		return WorldPresets.createNormalWorldDimensions(registries).replaceOverworldGenerator(registries, new FlatLevelSource(flat));
	}
}
