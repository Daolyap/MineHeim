# Engineering notes

What broke, surprised or cost time while building MineHeim, kept so the next person (or the next game update) does not pay twice.

## Valheim and Unity

- **Input.** Valheim reads keys through Unity's new Input System (`ZInput` on `Keyboard.current`). BepInEx's `KeyboardShortcut` (legacy
  `UnityEngine.Input`) is unreliable there; use `ZInput.GetKeyDown(KeyCode, false)` or `Keyboard.current` directly.
- **`OnPreRender`.** A MonoBehaviour method literally named `OnPreRender(Camera)` makes Unity log a script error; subscribe
  `Camera.onPreRender` with a differently named method.
- **The render thread.** Valheim renders one frame behind its main thread, so "the newest pose" is not the one on screen. A native
  plugin event queued in the main camera's command buffer runs on the render thread exactly when that camera is drawn: that is how the
  compositor knows which pose it is drawing.
- **Depth.** Valheim 1.0 runs Direct3D 11 with reversed-Z; ReShade sees its depth upside down (`RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1`).
  `DepthCopyBeforeClears=1` in `ReShade.ini` makes the generic depth add-on pick the right buffer without UI tweaks.
- **Debug fly is a trap.** `Character.UpdateDebugFly` gives exactly the right flight, but real debug-fly mode makes the player invulnerable
  and marks every creature he hits as "cheated" (no drops). MineHeim's flight runs the same motion step from a prefix on `UpdateMotion`
  without the mode.
- **Fall damage after flying.** Flight keeps the last ground contact at zero, so `IsOnGround()` is true at once; the no-fall-damage grace
  must wait for a real, settled touchdown.
- **`HitData.m_blockable` defaults to false.** A forwarded hit must set it or no shield will ever block it.
- **Backstab on stand-ins.** A creature body with its AI disabled is never "alerted", so Valheim applies the backstab bonus (x3) to every
  hit on it; push its backstab timer far ahead. `HitData.GetTotalDamage()` also includes chop and pickaxe damage meant for trees and
  rocks: leave those out when converting a hit on a creature.
- **Non-persistent clones.** Instantiating a networked prefab saves it into the world. Turning the prefab's `ZNetView.m_persistent` off
  around the `Instantiate` call gives a clone that is never saved.
- **No-build areas.** The start temple is a no-build location: Valheim's own pickaxe cannot dig there, and neither can MineHeim's. Tests
  that dig must start elsewhere.
- **BepInEx config strings unescape backslashes.** A path like `D:\Mods\0.9\run.cmd` loses characters (`\0`); write `/` or `\\`.

## Minecraft 26 and Fabric

- **SDL3, not GLFW.** Minecraft 26 moved its window and input to SDL3: key events carry USB scancodes (`E` = 8, `Esc` = 41), mouse buttons
  are 1 left / 2 middle / 3 right, modifiers are SDL masks (Shift 0x1, Ctrl 0x40, Alt 0x100). Injecting GLFW codes silently does nothing.
- **Unfocused window, frozen hover.** Minecraft only passes cursor movement to a screen while its window has focus
  (`handleAccumulatedMovement` checks `isWindowActive`); a small redirect lets the host's forwarded cursor through.
- **Hidden windows stop rendering.** `SDL_HideWindow` (and minimising) stops Minecraft's frames; so does parking the window far off-screen
  in practice. Keep it on screen behind the host, make it a tool window (out of the taskbar and Alt-Tab), and show it again when the host goes.
- **`SO_REUSEADDR` on Windows** lets a second Minecraft bind the same port and steal the host's messages: don't set it there.
- **`fill` is capped at 32,768 blocks**, and `fill ... hollow` empties the inside of the box (it would delete ground barriers inside a ring).
- **A barrier must not be breakable.** In Creative one click at the ground would delete the barrier under it (the pick ray uses outline
  shapes); `ServerPlayerGameMode.destroyBlock` refuses barriers while a host is attached.
- **Relative teleports** carry per-axis offsets in `TeleportTransition.relatives()`; resolve them before telling the host.
- **Command feedback is off** in the MineHeim world (`send_command_feedback false`), so a successful `/give` prints nothing: test the
  inventory, not the log.

## Tooling and testing

- **Pair screenshots with poses by data, never by time.** ReShade saves screenshots asynchronously, 1-3 frames later; the compositor stamps
  both frame numbers as a barcode into the corner of the picture and the oracle decodes it.
- **Wait for a new log line.** A status hook's answer is written a moment later; reading "the last status line" gets the previous one.
- **One Minecraft at a time.** Two clients (an auto-started one and a manual one) both connected and the tests misbehaved in confusing ways.
- **ReShade FX has no `fmod`**, and effect compile errors only appear in `ReShade.log` (the technique silently disappears).
- **The add-on reloads the installed `.fx` within a second**, so shader work needs no game restart; the add-on DLL itself is locked while
  Valheim runs.
- **No admin, no compiler.** `tools/get_msvc.py` fetches a portable MSVC + Windows SDK from Microsoft's own manifests (hash-checked).
  MSVC, not MinGW: ReShade's add-on API is C++ virtual classes whose layout depends on the compiler.
- **ReShade's installer carries an appended zip** whose offsets count from the zip's own start: find the end record, compute the start,
  slice, then open it.
- **Windows PowerShell started from PowerShell 7** inherits its module path and loses `Get-FileHash`; the `.cmd` wrappers clear
  `PSModulePath` first.
