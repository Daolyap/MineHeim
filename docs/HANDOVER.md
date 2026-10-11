# Handover: MineHeim after play test round 2 (2026-10-11)

For the AI picking this up on the owner's Windows PC, where the real Valheim and Minecraft installs, logs and saves are.
Read this first, then `README.md`, `docs/HOW-IT-WORKS.md`, `docs/CONFIGURATION.md` and `docs/ENGINEERING-NOTES.md` (what broke
before and why: read it before touching input, rendering or the launcher).

## Ground rules from the owner

- Work on branch `v1/nether-infection`; push only there. Draft PR: https://github.com/Daolyap/MineHeim/pull/2.
- The passthrough design (Valheim hosts, Minecraft runs hidden behind it, ReShade composites) stays. Don't propose a rewrite.
- Valheim has no first person. Don't build for it.
- Steve's skin is classic Steve by default; `Steve.Skin = Account | File` is the opt-in.
- Goal: **the game is playable as Steve from start to finish**, and Minecraft mode should feel like part of Valheim, not a separate game
  you switch to.
- Untested checks are kept for later (the owner stopped testing early). The list is at the end.

## What MineHeim is (short)

- `valheim/`: the BepInEx plugin (C#, net472, Unity 6). `valheim/Core` has the Unity-free logic plus `Core.Tests` (a console test runner).
- `mc/`: the Fabric mod for Minecraft 26.3 (Java 25, Gradle; on a fresh clone run it as `bash gradlew` / `gradlew.bat`).
- `compositor/`: the ReShade add-on (C++, MSVC) and `MineHeim.fx`.
- The link is a WebSocket on 127.0.0.1:25599; messages are one-line JSON `{"t":...}`. The list is in the doc comment at the top of
  `mc/src/client/java/dev/daolyap/mineheim/client/HostLink.java` and in `valheim/MineHeim/PoseSender.cs` (dispatch).
- Coordinates: Minecraft (x, y, z) = Valheim (x, y + 70, -z) (`valheim/Core/Mapping.cs`).
- Build and install: `.\Install-MineHeim.ps1 -Build -Only Valheim,Minecraft` (add `Compositor` for the C++). Release: `tools\make_release.ps1`
  (plugin and mod versions must match; both are 1.0.0).
- Logs: `Valheim\BepInEx\LogOutput.log` (look for `perf:` lines and `[Info : MineHeim]`), and Minecraft's `logs/latest.log` in its instance folder.

### What changed in the last two sessions (all on the branch)

- **Valheim now draws Steve itself** (`valheim/MineHeim/SteveAvatar.cs`, `valheim/Core/SteveModel.cs`): a 64x64-skin player model
  built in Valheim, animated with Minecraft's own maths (walk, swing, crouch, sit, swim, shield up). Minecraft's frame arrives 1-3
  frames late; re-projection fixes still blocks but not a moving Steve, so he trailed. Drawn by Valheim, he doesn't. Confirmed in
  round 2 (G12: "difference is massive"). `Steve.Avatar = Minecraft` or **F11** brings the old way back.
- Skin: Minecraft sends the PNG (`SkinSender.java`); the plugin can also load a file.
- Arm swings: `LivingEntityMixin` hooks `LivingEntity.swing` and sends `{"t":"armswing"}`.
- Hotkeys: **F10** status (and restarts Minecraft if it died), **F11** who draws Steve, **Alt + wheel** zooms Valheim's camera in
  Minecraft mode. F7 Viking/Steve and F8 Creative/Survival were there already.
- End dragon lifecycle fixes, mobs leave healed land, relit portals spread again straight away (`NetherInfection.java`).

## Round 2 results (test sheet: https://claude.ai/artifact/3ZujziDppsXU6ATWHUJJhy)

62 of 104 checks marked: 54 pass, 2 fail, 6 skip. The rest weren't reached. Owner's notes are quoted.

| Check | Result | Note |
|---|---|---|
| S1 perf | pass | `66.2 fps ... minecraft 9.2 frame(s) behind, steve lead 155 ms, steve offset 23.0 cm`, later `51.4 fps ... 1.2 frame(s) behind, 22 ms, 0.0 cm`. The first line is a load spike; watch for it. |
| S2-S4 | pass | Log says `MineHeim 1.0.0 loaded`. |
| G1-G8, G11 | pass | G1 "Steve looks absolutely perfect." |
| G9, G10 | skip | Prediction / re-projection off comparisons. |
| G12 | pass | "Difference is massive. Only issue is that Steve doesn't hold items when drawn the new way." |
| A0 | pass | Builds on the real Valheim. |
| A1 | pass | `steve: drawn by Valheim with the Legacy Shaders/Diffuse shader` (not Standard: Valheim strips it, the fallback works). |
| A2-A4, A6, A8, A11-A13 | pass | |
| A5 | pass | "He stood at a ship helm, but the logic works." (expected; maybe give him a helm pose later) |
| A7 | pass | "I can't look at Steve's front. He always turns with the camera." See issue 9. |
| A9, A10 | skip | Account skin, skin file. |
| A14 | pass | Shield pose works, no shield model. Owner asks: "Do we really have to create models for everything, or can we pull them from Minecraft?" See issue 2. |
| C1, C2, C4, C6, C7 | pass | |
| C3 | pass | "Looks better but blocks still wobble / ghost a little bit." See issue 10. |
| C5 | pass | "Anything in Minecraft disappears [when a Valheim menu is open]. Not really an issue, it keeps the menu on top." |
| L1, L2, L5 | pass | |
| **L3** | **fail** | "Java SE Binary does not exit." after quitting Valheim from the menu. See issue 1. |
| **L4** | **fail** | Minecraft doesn't close after Valheim is ended in Task Manager. See issue 1. |
| L6 | skip | F10 restart after L5. |
| K1-K5, K7-K9 | pass | |
| K6 | skip | "Didn't test. Include in the next one." |
| B, F, M, I, W, N, E, P | not reached | Keep for the next round. |

## Issues and requests, in priority order

### 1. Minecraft doesn't exit with Valheim (L3, L4 fail)

After Valheim quits (from the menu, or killed), `javaw`/"Java(TM) Platform SE binary" stays in Task Manager.

- Where: `valheim/MineHeim/MinecraftLauncher.cs` (`OnQuit`, and the hello message carrying `"host":<pid>,"quitWithHost":...`) and on the
  Minecraft side the host-watch thread (search `quitWithHost` / the polling thread that replaced `ProcessHandle.onExit`; see the
  ENGINEERING-NOTES entry about the process reaper).
- Check: is `Link.QuitWithValheim` true in the config? Does Minecraft's `latest.log` show the quit message arriving, or the watch
  thread noticing the host PID gone? Does it start saving and then hang (a non-daemon thread, e.g. the new `MineHeim skin` thread is
  daemon but check others, or the integrated server not stopping)?
- Probably needs: after a save, a hard `Runtime.halt` / `System.exit` with a timeout, and the PID watch must not depend on the link.
- Then re-run L3, L4, L5, L6.

### 2. Steve holds nothing when Valheim draws him (G12, A14)

The native Steve has no held item, off-hand item, shield or armour. The owner wants this fixed, and asked whether models must be made
by hand. **They don't: pull them from Minecraft.**

Suggested approach:
- Minecraft side: when the main-hand / off-hand / armour items change (and on connect), resolve each item's model the way Minecraft
  renders it in third person (the item model's baked quads for the `THIRD_PERSON_RIGHT_HAND` / `LEFT_HAND` display context, including
  its display transform) and send the geometry: positions, UVs and the texture (the sprite cut out of the block/item atlas as a PNG,
  or the item's own texture). Flat items (swords, tools) are an extruded sprite; Minecraft builds that mesh itself, so send its
  quads, don't rebuild it. Special-renderer items (shield, trident, banners, chests) use entity models: send their model parts like
  the player model, or start with a per-item fallback (a textured box) for those.
- New message, e.g. `{"t":"held","hand":"main|off","mesh":{...},"png":b64,"xf":[...]}`; cache by item id + components so it's sent once.
- Valheim side: build a Mesh, parent it to the arm transform in `SteveAvatar.cs` at Minecraft's hand pivot (the arm's end, offset as in
  `ItemInHandLayer`), apply the display transform. Same material path as the body.
- Armour: humanoid armour layers are the same box model as the player at inflated sizes with the armour texture; reuse `SteveModel.Build`
  with an inflation parameter and the armour texture sent as PNG. Trims and dye tint can come later.
- Unit tests in `valheim/Core.Tests` for any mesh/transform maths that lives in Core.

### 3. Weapon balance: playable as Steve start to finish

Owner: "The fist does way too much damage, and weapons don't scale well. It's extremely easy to spam-kill anything, but extremely
difficult to kill a boss with proper strategy." Also asked: can Minecraft weapons get the stats of a Valheim weapon, so a mace on a
blob does much more damage?

How it works now:
- `ClientInput.swing` (Minecraft side) sends `{"t":"melee","item":...,"dmg":...}`, with the held item's attack damage scaled by the recharge
  (`0.2 + 0.8 * charge^2`).
- `WorldEvents.OnMelee` (Valheim side) multiplies by `MobBridge.Rate()` = max(Viking max health / 20, `Events.WeaponPointDamage` = 6)
  times `Events.WeaponDamageScale`, and hits every creature in a cone. Damage type comes from the item name: `_sword`/`_axe` slash,
  `_pickaxe`/`_spear`/trident pierce, anything else (fist, mace, block) blunt.
- So **damage types already exist, and Valheim's own resistances and weaknesses already apply** (`Character.Damage` → damage modifiers).
  The weak points:
  - The amount ignores the target and the weapon's tier. A fist is worth 1 point × 6 = 6 at least, and the rate grows with food, so a
    fist gets *stronger* as you eat better.
  - Spam costs nothing: no stamina, and the cone hits several creatures at once.
  - No Valheim skill levels, stagger damage, backstab rules, or chop/pickaxe tiers.
  - Bosses have thousands of health and real resistances. Steve's damage never gets near that, so fights are long and unrewarding.

Proposal (the owner asked whether this is possible; confirm the details with them before building):
- **Steve's weapons borrow a real Valheim weapon's stats.** Add a table in Core (like `ItemMap.cs`) mapping each Minecraft
  weapon to a Valheim prefab by material tier and kind. Example: wooden/stone → Meadows/Black Forest, iron → Swamp, diamond →
  Mountains/Plains, netherite → Mistlands/Ashlands. Sword → sword line, axe → axe line, mace → mace line, trident/spear → spear,
  bow/crossbow → bow/crossbow. Read the prefab's `ItemDrop.m_itemData.m_shared` from `ObjectDB` at run time (`m_damages`,
  `m_attack.m_attackStamina`, `m_attackForce`, `m_backstabBonus`, `m_skillType`, `m_toolTier`) so vanilla balance and future patches
  carry over.
- Apply it the way Valheim does: `HitData.m_damage = damages × skill factor` (`player.GetSkillFactor(skill)`, with
  `player.RaiseSkill` on hit), use the Viking's stamina per swing (no stamina: weak swing), and set `m_staggerMultiplier`,
  `m_pushForce`, `m_backstabBonus`, `m_toolTier`, `m_skill`. Keep Minecraft's recharge as an extra multiplier, so spam is weak and
  stamina-limited.
- Fist: map to Valheim's unarmed (`PlayerUnarmed` / Unarmed skill). Low blunt, so no more 6+ damage punches that grow with food.
- Enchantments: Sharpness, Smite, Fire Aspect etc. become bonus damage or elemental damage (fire, spirit vs undead). Keep it simple.
- Keep `WeaponPointDamage` / `WeaponDamageScale` as the fallback for unmapped items and as a global multiplier.
- Arrows (`MobBridge` / `WorldEvents` ranged path) get the same treatment from the bow tier.
- Hit one target, not the whole cone, unless the Valheim weapon's attack is an area attack.
- Write a small balance test in `Core.Tests` (Steve's tier-N weapon vs a Valheim tier-N weapon on a few creatures and bosses).

### 4. Minecraft mobs attack the Viking when F7 is off

Owner: they spawn, you can hit them, so they should fight back while you're the Viking too.
- Minecraft's player follows the Viking's position in both modes. Likely causes: the server player stays in Creative (`MineHeimClient.applyGameMode`;
  Creative players are never targeted), and/or Steve is hidden/excluded while not in Minecraft mode.
- Fix: while F7 is off, make the Minecraft player targetable (Survival or Adventure, or a mob `setTarget` like `Creatures.hunt` does for
  creature proxies), and make sure their hits reach the Viking. Mob → Viking damage already exists for Survival (`MineHeimClient`
  health sync / the `hurt` path); check it works in Viking mode and respects Valheim armour and blocking
  (`ShieldBlock.cs`, `HitData.m_blockable = true` per ENGINEERING-NOTES).
- Creative (F8) should still mean untouchable.

### 5. Valheim hotbar shows in Minecraft mode, and eating as Steve

- In Minecraft mode Valheim's hotbar (`HotkeyBar` in `Hud`) is useless; hide it while Minecraft mode is on (keep health, stamina
  and food icons).
- Eating in Survival: Valheim food is what keeps the Viking alive and raises max health. Steve currently has no way to eat. Options,
  best first:
  - Minecraft food eaten by Steve feeds the Viking: map Minecraft foods to Valheim food buffs of similar tier (bread → Meadows tier,
    cooked beef → boar meat, golden apple → a top-tier food, etc.) via `Player.EatFood` with a matching `ItemDrop` or a built status.
  - And/or Valheim foods cross to Minecraft as items (extend `ItemMap.cs`), so eating one in Minecraft eats the Valheim food.
  - Keep Minecraft's hunger bar mirroring the Viking's food (already done: `vikingFood`).

### 6. Make Minecraft mode feel seamless (the owner's bigger ask)

"More of a seamless thing rather than Minecraft or not Minecraft." Concrete items so far: issues 2, 3, 4, 5, plus:
- Valheim's inventory and Minecraft's should feel like one: at least Tab shows both, or Valheim items usable from Steve's hotbar.
- Valheim crafting stations, building and the hammer reachable as Steve.
- Ask the owner which of these matter most before building them.

### 7. Minecraft music muted by default

The owner finds Minecraft's music annoying, and Minecraft's window is hidden, so there's no easy way to turn it down. In
`MineHeimClient.configure` (it already sets options on first run), set `options.getSoundSourceOptionInstance(SoundSource.MUSIC).set(0.0)`
(check the 26.3 name for the sound-source volume option). Also add a plugin config entry, e.g. `Audio.MinecraftMusic` (0-1, default 0)
and `Audio.MinecraftVolume` (master), sent over the link, so it's adjustable from Valheim's side.

### 8. Easter egg: Herobrine eyes

"There's a conspiracy that we were Herobrine all along, since Steve is infecting loads of games." Give Steve Herobrine's
white eyes.
- Valheim-drawn Steve: after the skin loads, recolour the eye pixels on the head's front face (in Steve's 64x64 skin the face is
  x 8-15, y 8-15; the eyes are row 12, two pixels each side; check against the real `steve.png`) to white. Make them glow: either
  an emissive material on those quads, or two small unlit quads just in front of the face.
- Minecraft-drawn Steve (F11 / `Steve.Avatar = Minecraft`): do the same to the skin texture Minecraft uses, so both match.
- Config `Steve.HerobrineEyes` (on by default, or only at night or rarely; ask the owner). Only touch the default skin's eye pixels
  for account and file skins if the owner wants that too, since other skins put eyes elsewhere.

### 9. Can't see Steve's front (A7)

Valheim's third-person camera turns the character with it, so you can't look at Steve's face. Optional: a free-look hold key (camera
orbits, Steve keeps facing his way), or follow Valheim's own behaviour (the Viking also turns). Low priority; ask first.

### 10. Blocks still wobble a little (C3)

Re-projection made Minecraft's blocks steady, but there's a small remaining wobble and ghosting while moving. Candidates: depth
re-projection precision, the frame Minecraft rendered for vs the pose used (the barcode pairing in the compositor), and fps caps (S1
showed 9 frames behind under load). Measure with the perf line and the compositor's barcode before changing anything.

## Still to test (next round)

Skipped: G9, G10, A9, A10, K6, L6. Not reached: B1-B6, F1-F6, M1-M4, I1-I3, W1-W3, N1-N10, E1-E7, P1-P3.
After fixes above: re-test L3-L6, G12 with held items, A14 with a shield model, and new checks for each issue (balance, mob
aggression in Viking mode, hidden hotbar, eating, music, Herobrine eyes).

Add those new checks to the test sheet (the artifact link above) if you can publish to it; otherwise write them into
`docs/TESTING.md` and the owner will carry them over.
