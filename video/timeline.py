"""Both videos as one list each: rendered segments and numbered gameplay slots, with the voiceover for every one.
build.py renders the segments, makes a placeholder slate for every slot, joins them into a full-length draft, and writes
the script with timestamps and the shot list."""
from __future__ import annotations

from dataclasses import dataclass, field

import scenes as s


@dataclass
class Seg:
    scene: object          # an engine.Scene
    vo: str = ""


@dataclass
class Slot:
    """Your gameplay goes here: `footage` says what to record (the footnote in the shot list)."""
    number: str
    seconds: float
    title: str
    footage: str
    vo: str = ""
    overlay: tuple | None = None  # (title, line): a transparent lower third rendered for this clip


def general():
    F = s.feature_card
    return [
        Slot("G1", 7, "Cold open",
             "Third person, golden hour in a Meadows clearing. Start on the Viking's back, Steve visible where he stands. Pan to a Minecraft "
             "cottage between Valheim trees, then a TNT block next to a Valheim house goes off: crater, planks flying. Cut on the blast.",
             vo=""),
        Seg(s.hook(4.0), "This is real Minecraft. Running inside Valheim."),
        Seg(s.logo_sting(4.5), "This is MineHeim."),
        Slot("G2", 13, "What it is",
             "Walk Steve through Valheim for a few seconds, press F7, place a short line of blocks on uneven ground, break one. Show both "
             "hotbars: Valheim's off, Minecraft's on.",
             vo="Not a texture pack, not a remake. The actual Minecraft game runs next to Valheim, and everything it draws lands in "
                "Valheim's world. Steve stands exactly where my Viking stands."),
        Seg(s.explainer_simple(26.0),
            "Here's the trick. Every single frame, Valheim sends Minecraft its camera, its ground, its buildings and its creatures. "
            "Minecraft renders its world from exactly that point of view and hands the picture back, with depth. So every pixel shows "
            "whichever world is nearer. That's why Valheim's trees can stand in front of a Minecraft cottage."),
        Seg(F(1, "Build in Valheim", "Minecraft blocks on Valheim's ground", duration=2.5)),
        Slot("G3", 10, "Building",
             "Build a small Minecraft hut on a slope in Creative (fast), then switch F7 off and walk the Viking into its wall: he stops. "
             "A Greydwarf bumping into it is a bonus.",
             vo="You can build anywhere. The blocks sit on Valheim's terrain, and your Viking, and every Greydwarf, bumps into them.",
             overlay=("Build in Valheim", "Minecraft blocks stand on Valheim's terrain and are solid to everything")),
        Seg(F(2, "TNT means it", "Explosions wreck Valheim's world", duration=2.5)),
        Slot("G4", 10, "TNT",
             "TNT (key 4, light with key 5) next to a Valheim wood house and a big tree. Stand back. Show the house breaking, the tree "
             "falling, and the crater afterwards. A second angle on the crater if you have it.",
             vo="And TNT means it. Minecraft explosions wreck Valheim buildings, fell trees, crack rocks and dig real craters.",
             overlay=("TNT means it", "Buildings break, trees fall, the ground craters: through Valheim's own damage rules")),
        Seg(F(3, "Minecraft weapons", "Real Minecraft damage, Valheim's scale", duration=2.5)),
        Slot("G5", 11, "Weapons and shield",
             "Sword on a Greydwarf (a couple of charged hits), a bow shot at a Troll or Greyling, then hold right mouse with the shield "
             "facing a Troll: the 'Blocked' and the Troll staggering.",
             vo="Minecraft's weapons hit for their real damage, converted to Valheim's scale. And the shield? It stops a Troll dead.",
             overlay=("Minecraft weapons", "Sword, bow, TNT, ender pearls, and a shield that stops a Troll")),
        Seg(F(4, "Mobs vs creatures", "Two bestiaries, one fight", duration=2.5)),
        Slot("G6", 13, "Mobs vs creatures",
             "Creative, near Greydwarfs (Black Forest edge is ideal): place a few creeper and zombie spawn eggs. Let the zombies swarm a "
             "Greydwarf, a creeper blow up next to one, and a Greydwarf hit a zombie back. Night makes the zombies feel at home.",
             vo="This is my favourite. Minecraft's monsters hunt Valheim's creatures, and Valheim's creatures fight back. Two bestiaries, "
                "one fight.",
             overlay=("Mobs vs creatures", "Minecraft's monsters hunt Valheim's creatures, and they fight back")),
        Seg(F(5, "Items cross over", "Moved, never copied", duration=2.5)),
        Slot("G7", 10, "Items",
             "Survival. Drag Wood out of the Viking's inventory: it lands as oak logs and Steve picks them up (show Minecraft's hotbar). "
             "Then F7, pick cobblestone, press Q: it lands as Valheim Stone and the Viking picks it up (show the pickup message).",
             vo="Items cross over. Throw Valheim wood and it lands as Minecraft logs. Throw cobblestone back and it's Valheim stone. Moved, "
                "never copied.",
             overlay=("Items cross over", "Wood becomes oak logs, cobblestone becomes Stone: about 45 pairs")),
        Seg(F(6, "Your base is solid", "Valheim buildings block Minecraft mobs", duration=2.5)),
        Slot("G8", 9, "Buildings",
             "Stand inside a Valheim house with the door shut; spawn a zombie outside: it can't get in. Open the door: it walks in.",
             vo="Valheim's buildings are solid to Minecraft too. Zombies stop at your walls... until you open the door.",
             overlay=("Your base is solid", "Valheim walls, floors, roofs and doors are Minecraft collision")),
        Seg(F(7, "One light", "Shared day, night and fire", duration=2.5)),
        Slot("G9", 10, "Light",
             "Dusk into night by a Minecraft build (Steve's blocks darken with the scene). Light a Valheim campfire next to Minecraft "
             "blocks, then place Minecraft torches and show Valheim's grass lit around them.",
             vo="Day and night are shared. Valheim's fires light Minecraft's blocks, and Minecraft's torches light Valheim.",
             overlay=("One light", "Minecraft's sun follows Valheim's; fires and torches light both worlds")),
        Seg(F(8, "Minecraft's screens", "Inventory, creative tabs, chat", duration=2.5)),
        Slot("G10", 9, "Screens",
             "F7, Tab: the creative inventory over Valheim; drag a few blocks into the hotbar. Then T and a command (/give @s torch 16).",
             vo="And Minecraft's own inventory, creative tabs and chat work right on top of Valheim.",
             overlay=("Minecraft's screens", "Tab for the inventory, T for chat and commands")),
        Seg(F(9, "Fly, or survive", "Creative and Survival, one key apart", duration=2.5)),
        Slot("G11", 8, "Flight",
             "Double-tap Jump and fly up over the Meadows, then land. Press F8 to show the Creative/Survival switch message.",
             vo="Creative flight, Survival rules, one key to switch.",
             overlay=("Fly, or survive", "Double-tap Jump to fly; F8 switches Creative and Survival")),
        Slot("G12", 8, "Montage",
             "Your best 6-8 seconds: fast cuts of the funniest moments (a creeper at a Troll, a cottage at sunset, a crater).",
             vo=""),
        Seg(s.install_steps(24.0),
            "Want it? It's free and open source. Download the zip, run Install.cmd, sign in to Minecraft once, and start Valheim. "
            "Minecraft starts by itself in the background."),
        Seg(s.outro(20.0),
            "The link's in the description. It was built with AI, and I break down exactly how it works in the next video. Tell me what "
            "we should cross over next."),
    ]


def technical():
    C = s.chapter
    return [
        Seg(s.tech_title(8.0), "This is how MineHeim works: real Minecraft, inside Valheim. Nine parts, about eight minutes."),
        Slot("T1", 15, "Recap",
             "15 seconds of the best gameplay from the first video (or reuse its montage).",
             vo="Quick recap: Steve in Valheim, his blocks on Valheim's ground, his mobs fighting Valheim's creatures. Two real games at "
                "once. So how do two unrelated engines share one world?"),
        Seg(C("Architecture", 1, "two processes, one socket, two shared memories")),
        Seg(s.architecture(50.0),
            "Two processes. Valheim, a Unity game, gets a BepInEx plugin. Minecraft, a Java game, gets a Fabric mod. They talk over a "
            "WebSocket on localhost: Valheim sends its camera every frame, plus its terrain, buildings and creatures; Minecraft sends "
            "back explosions, swings, arrows, hits and items. The heavy part is the picture. Minecraft renders into shared memory, and "
            "a ReShade add-on inside Valheim's own process picks it up. To know which frame Valheim is drawing, the plugin drops a "
            "marker into the camera's command buffer, which runs on Valheim's render thread. Valheim is the host: its camera, its "
            "terrain, its health and its saves are the truth."),
        Seg(C("One camera", 2, "two coordinate systems")),
        Seg(s.mapping(30.0),
            "Valheim is left-handed with z pointing north; Minecraft is right-handed with z pointing south. So one axis flips. The 70 "
            "lifts Valheim's sea level to Minecraft's y 100, and it never changes, because Minecraft's world persists: your builds stay "
            "on the same hill every session. I checked the camera maths against Minecraft's own code on fifty thousand random "
            "orientations."),
        Seg(C("One picture", 3, "layers, depth test, re-projection")),
        Seg(s.picture(46.0),
            "Minecraft renders three layers: colour, depth, and an overlay with the hand, the hotbar and any open screen. Inside "
            "Valheim, the shader compares Minecraft's depth with Valheim's own depth buffer, pixel by pixel, and shows whichever world "
            "is nearer. That's why grass grows over the bottom of a Minecraft wall. But Minecraft's frame is always one to three frames "
            "behind, so the shader re-projects it to Valheim's newest camera. Measured while strafing: about one pixel of error with "
            "re-projection, five and a half without."),
        Slot("T2", 10, "Depth in action",
             "Walk slowly around the Minecraft cottage so Valheim trees pass in front of it, and walk through tall grass next to blocks.",
             vo="And here's the depth test live: trees in front, blocks behind, grass growing over both."),
        Seg(C("One ground", 4, "both ways")),
        Seg(s.ground(44.0),
            "Minecraft's world is a void. The plugin fires raycasts down Valheim's terrain, a hundred and sixty columns a frame, and "
            "Minecraft fills the two cells under each surface with invisible barrier blocks. Your blocks, items and mobs rest on "
            "Valheim's ground. Valheim's buildings are tested cell by cell against their own colliders and become barriers too, "
            "reference-counted, so a floor on the ground never deletes the ground under it. A door that opens gets voxelized again. "
            "And it works backwards: every Minecraft block near you becomes a box collider in Valheim."),
        Slot("T3", 8, "Collision both ways",
             "The zombie stopping at a Valheim wall and walking in through the open door; the Viking walking into a Minecraft wall.",
             vo=""),
        Seg(C("One fight", 5, "stand-ins and one exchange rate")),
        Seg(s.fight(48.0),
            "Each game only knows its own creatures, so each gets stand-ins. Valheim's creatures exist in Minecraft as invisible "
            "entities of their size; Minecraft's monsters target them, and their hits are sent back. Minecraft's mobs get invisible "
            "bodies in Valheim, cloned from a creature with its looks, AI and loot stripped out. Valheim's creatures attack them, and "
            "your Viking can hit them with Valheim weapons. Damage crosses at one rate: a Minecraft point is the Viking's max health "
            "divided by twenty. Testing caught a fun bug: every hit on a body counted as a backstab, triple damage, and included the "
            "damage meant for chopping trees."),
        Slot("T4", 10, "Fight",
             "A zombie swarm on a Greydwarf, the Greydwarf hitting back, a creeper exploding next to Greylings.",
             vo=""),
        Seg(C("Items that move", 6, "a two-phase handshake")),
        Seg(s.items(38.0),
            "Items must never duplicate. When Steve throws something, Minecraft offers it. Valheim checks its table and asks for the "
            "part that crosses, four planks for one wood. Minecraft takes them away, only if they're still lying there, and confirms. "
            "Only then does Valheim create the wood. Pick the item up in between and nothing crosses."),
        Slot("T5", 8, "Items", "The Wood -> oak logs drop and the cobblestone -> Stone throw, close up.", vo=""),
        Seg(C("Light and screens", 7, "and starting together")),
        Seg(s.light_and_screens(38.0),
            "Minecraft's clock follows Valheim's day, Valheim's fires become invisible Minecraft light blocks, and Minecraft's torches "
            "become Unity point lights. Minecraft's screens just work over Valheim: the cursor, clicks and typed text are forwarded. "
            "One lesson: Minecraft 26 moved to SDL3, so keys are USB scancodes now, not GLFW codes. And Valheim starts Minecraft through "
            "Prism Launcher, keeps its window out of the way, and closes it when you quit."),
        Slot("T6", 10, "Light and screens", "Night with torches lighting Valheim; the creative inventory open over Valheim.", vo=""),
        Seg(C("Testing", 8, "oracles, not eyeballing")),
        Seg(s.testing(30.0),
            "Every feature was built against a measurement. Fifty thousand camera orientations against Minecraft's own maths. A gold "
            "pillar measured against Unity's projection, to about a pixel. And scripts that drive both real games through test hooks: "
            "nineteen out of nineteen checks for the creature and item systems, six out of six for the screens and auto-start."),
        Seg(C("Built with AI", 9, "what that means here")),
        Seg(s.built_with_ai(26.0),
            "MineHeim was built with AI. Claude Code wrote and tested the code; I set the goal and the rules, and played it. Single "
            "player only, never get around Minecraft's ownership check, ship no game files, back up first. It started from the "
            "open-source universal-modder project, and stands on BepInEx, ReShade, Fabric and Prism Launcher."),
        Seg(s.outro(20.0, technical=True),
            "Everything's on GitHub, including the engineering notes and every test. Thanks for watching."),
    ]


VIDEOS = {"general": general, "technical": technical}
