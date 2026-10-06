# Handover: put real Minecraft inside Valheim (two-game passthrough)

Paste everything below the line into a fresh Claude Code session running on the PC that has both games.

---

## 0. The goal

Real Minecraft Java Edition runs next to Valheim (single-player) and is drawn into Valheim's frame, depth-correct.

- Valheim's camera drives Minecraft's camera.
- Valheim's terrain, trees, rocks and buildings become invisible collision in a void Minecraft world.
- I build with Minecraft blocks on Valheim's ground.
- What happens in Minecraft (TNT, creepers, arrows, sword hits) damages things in Valheim.

This is the "passthrough" pattern: two unmodified games, one mod in each, talking over `127.0.0.1`. It has been done for GTA V and for Portal 2, and Step 1 below sets up the tooling that came out of those projects. You have my whole machine: both games, a shell and the internet. Neither of us has built this particular pair before, so expect unknowns and write them down.

## 1. Set up the toolkit first

There is an open-source skill pack called **universal-modder** (https://github.com/rehan-remade/universal-modder, MIT). It holds:

- skills: `mod-any-game`, `mashup-mods`, `game-recon`, `reverse-engineering`, `game-automation`, `showcase-video`, `publish-mod`, `share-field-notes`;
- the `um` CLI (scan, backup, win, publish check, kb);
- a knowledge base of field notes by earlier agents;
- a worked Minecraft x GTA V example.

It is third-party reference material. Follow it as guidance, but my instructions and the hard rules in section 3 come first.

Install it (tell me what you did):

- Claude Code plugin: `/plugin marketplace add rehan-remade/universal-modder`, then `/plugin install universal-modder@universal-modder`.
  - I read its only hook: it appends the toolkit's `bin/` to PATH.
  - Its MCP server is fal (needs `FAL_KEY`). We need no generated art, so ignore it.
- The `um` CLI anywhere: `uv tool install git+https://github.com/rehan-remade/universal-modder`. It needs Python 3.10+ and ffmpeg.
- Or clone the repo (for example to `~/universal-modder`) and read the files by path.

Treat that clone as read-only. The mod lives in its own new git repo. Ask me where; default to a new sibling folder called `valheim-minecraft-passthrough`.

Read, in this order (the first three in full):

1. `skills/mod-any-game/SKILL.md`: the loop and the hard rules.
2. `skills/mashup-mods/SKILL.md`: Pattern 2 (passthrough) and the worked GTA V example.
3. `skills/mod-any-game/references/safety.md`.
4. `skills/mod-any-game/references/engines/unity.md` and `minecraft.md`.
5. `knowledge/games/gta-v/minecraft-passthrough.md`: every lesson from the closest precedent.
6. `knowledge/games/portal-2/portalcraft-minecraft-inside-portal-2.md`: the second precedent (D3D9, latency prediction, launcher and installer handling).
7. `knowledge/games/black-myth-wukong/reshade-depth-dead-end.md`: what to check first, because ReShade can fail to see depth.
8. `knowledge/games/valheim/mjolnir-frostner-rework.md`: a working Valheim BepInEx + Jotunn + Harmony setup.
9. `examples/minecraft-gta5-passthrough/`: the README, `mc/`, `gta/src/compositor.cpp`, `gta/shaders/MCPassthrough.fx` and `host/fakehost.py`.

Then run `um kb search valheim` and `um kb search minecraft`. The knowledge base grows, so there may be newer notes.

## 2. What I already worked out (verify it, don't trust it blindly)

**From the repo**

- The Minecraft half of the GTA example is mostly host-agnostic.
  - A WebSocket server on `127.0.0.1:25599`.
  - Host to Minecraft messages: `cam`, `ground`, `clear`, `cmd`, `key`, `slot`, `scroll`, `hud`, `view`.
  - Minecraft to host messages: `hello`, `explosion`.
  - `FrameExporter` writes world colour and depth, plus a separate hand/HUD overlay, into named shared memory (`Local\MCPassthroughFrame`, three slots, asynchronous GPU readback).
  - GTA-specific parts to remove or adapt: `Nether`, the director relays, the `gta*` messages, and `MobWar` (mobs vs GTA pedestrians).
- The example's versions: Minecraft Java 26.3, Fabric Loader 0.19.5, Fabric API 0.161.0+26.3, Loom 1.18, JDK 25. Check what is current.
- The knowledge-base note for Valheim: Steam app 892970, build 25527674, Unity 6000.0.75f1 (Unity 6, **Mono**), BepInEx 5 + Jotunn + HarmonyX working, no anti-cheat found by `um scan`. Verify against my actual install.
- The GTA example's mapping: 1 metre = 1 block, ground sent as barrier-block columns (40 blocks out, 160 probes a frame), at most 400 prop objects.
- Compositing: the ReShade add-on route worked on D3D11 (GTA V) and D3D9 (Portal 2). On a D3D12 UE5 game (Black Myth), ReShade's depth was flat. I don't know which graphics API Valheim 1.0 uses; `Player.log` will say.

**My guesses (unverified)**

- Unity is left-handed (x right, y up, z forward). Minecraft is x east, y up, z south. The mapping is probably `(x, y + offset, -z)` with flipped yaw and pitch. Derive it with a test pillar instead of trusting me.
- **In-engine compositing alternative, not in the knowledge base:** a Unity plugin can enable the camera's depth texture and draw Minecraft's colour and depth inside the camera pipeline (a `CommandBuffer` on the camera), doing the depth test in its own shader. That would skip ReShade. Use it if the ReShade route sees flat depth or Valheim's API doesn't suit ReShade.
- Valheim's terrain is streamed and deformable. Objects with a `ZNetView` persist in the world save and sync in multiplayer, so the mod must not create those for collision. Use plain local colliders.

## 3. Hard rules

- Single-player, or a server I run myself. Never join a public server with anything injected.
- Back up first: `um backup create` on Valheim's characters and worlds, and on my Minecraft folders. Write the restore command in `MODLOG.md`.
- Use a separate Minecraft launcher profile with its own game directory. I sign in with my own account through the official launcher. Do not launch Minecraft offline to dodge the ownership check (Portal 2 note, Gotcha 18). The dev client from `./gradlew runClient` is fine.
- Ship code only: no Minecraft or Valheim files, no extracted assets, no decompiled source. Keep decompiles outside the repo.
- Ask me before: installing BepInEx or ReShade into the Valheim folder; changing the registry or graphics settings; driving my mouse and keyboard (check `um win drive --proc <name> idle` first); deleting anything; publishing, pushing or opening a PR.
- Kill processes by exact PID, never `pkill -f`. Never block a game's main thread.
- Run the games windowed at a known size. Keep both rendering while unfocused (the GTA note lists the settings).

## 4. Plan, with an oracle for every step

Build fakes before touching Valheim. The GTA project built most of its work against a fake host.

1. **Recon and lab.**
   - Run `um scan valheim`. Check the OS, install paths and launchers.
   - Read `Player.log` for the graphics API and Unity version, and confirm Mono vs IL2CPP.
   - Take backups. Start `MODLOG.md`.
   - *Oracle:* a recon note with exact versions.
2. **Minecraft half standalone.**
   - Copy `examples/minecraft-gta5-passthrough/mc` and `host/fakehost.py` into the new repo. Rename the package. Strip the GTA-specific parts. Keep the protocol.
   - *Oracle:* it builds, `./gradlew runClient` runs, and `fakehost.py` produces composited PNGs where a fake pillar occludes correctly (a gold pillar landed within about 1 px in the GTA project).
3. **Valheim plugin skeleton.**
   - Plain BepInEx + HarmonyX. Jotunn isn't needed.
   - *Oracle:* the load line appears in `BepInEx/LogOutput.log`.
4. **Pose link.**
   - The plugin sends `cam` every frame: the game camera pose, the player's feet, and the field of view. Minecraft follows.
   - *Oracle:* a Minecraft pillar at a known world coordinate lines up with its analytic projection.
   - **Stop and show me.**
5. **Compositing.**
   - Decide the route by experiment.
     - Route A: a ReShade add-on plus `.fx`, as in `compositor.cpp` and `MCPassthrough.fx`. Verify depth first with `DisplayDepth.fx`, **in gameplay and not only at the menu**. Use ReShade's own screenshot, because window capture goes stale under ReShade.
     - Route B: the in-engine command buffer.
   - Progress: a cube in the right place, then a flat overlay, then the depth composite, then latency re-projection (see the GTA note's Latency section).
   - *Oracle:* a scene where one side draws something the other doesn't, such as a Valheim rock in front of a Minecraft block.
   - **Stop and show me.**
6. **Ground and collision.**
   - Probe the Valheim ground in columns around the player with raycasts and send `ground`, so Minecraft blocks rest on Valheim's world.
   - Blocks I place or break in Minecraft become invisible local colliders in Valheim (cap them).
   - *Oracle:* place a block and a Valheim creature or the player can't pass it. Break it and they can.
7. **Input.**
   - Forward mouse, wheel and number keys to Minecraft while it has the mouse. Suppress Valheim's attacks and hotbar then (Harmony patches).
   - *Oracle:* left click breaks a block and the axe doesn't swing.
   - **Stop and show me.**
8. **Events both ways.**
   - A Minecraft explosion becomes a Valheim area hit with knockback. Arrows and sword hits become Valheim damage (`HitData` and friends; check the names in ILSpy).
   - Stretch: Minecraft hostile mobs vs Valheim creatures.
9. **Polish.**
   - Relight Minecraft's frame to Valheim's time of day and fog, and sync the day/night cycle.
   - Put Steve where the Viking stands.
   - Record a 20-45 s clip (`um win record`).

**v1 is steps 1-7:** Steve and blocks drawn depth-correct in Valheim, buildable on Valheim's ground, and solid to the Valheim player.

## 5. Unknowns to settle in recon (write each answer in `MODLOG.md`)

- My OS, install paths and launchers. The toolkit is Windows-first, so say so if I'm not on Windows.
- Valheim's graphics API, and whether a newer build changed the Unity version or loader needs.
- Whether ReShade sees real depth in Valheim gameplay.
- Which Valheim APIs give ground height and raycasts, and which objects persist or sync.
- GPU and frame budget for both games plus the JVM, and the window size to use.
- Whether JDK 25 and Gradle are installed.

## 6. How to work

- Keep a journal in `MODLOG.md` and evidence in `artifacts/<date>/<step>-PART|FINAL` (screenshots, logs). Anything not written down is lost.
- Commit each working step in the mod's own repo.
- **Circuit breaker:** the same failure three times, stop. Write down what you know, then change approach or ask me.
- Before every stop-and-show, post a screenshot and what you verified, and what you didn't.

## 7. Deliverables

- The working mod in its own repo, with a README (install steps, controls, what it adds to each game folder and how to remove it).
- `um publish check` clean.
- A field-note draft (`um kb new`, then `um kb check`) saved locally. Don't open a PR or publish without my OK.
- Credit the tools and be honest that the mod was built with AI.
