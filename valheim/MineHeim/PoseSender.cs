using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Step 4, the pose link: every frame, the pose Valheim's camera is about to render with goes to Minecraft as a
    /// "cam" message (Camera.onPreRender, so it includes camera shake and is the pose of the frame being prepared).
    /// Minecraft's camera follows it. Also: sizes Minecraft's window to Valheim's, and the alignment test pillar.
    /// </summary>
    internal sealed class PoseSender : MonoBehaviour
    {
        private static readonly FieldInfo DistanceField = AccessTools.Field(typeof(GameCamera), "m_distance");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        private Camera _camera;
        private long _frame;
        // the ReShade add-on's render-thread marker (see UpdateMarker)
        private IntPtr _renderEvent;
        private CommandBuffer _marker;
        private float _nextLookup;
        private bool _wasConnected;
        private int _viewW, _viewH;
        private int _received;

        // alignment oracle (F9): a Minecraft gold column at a known cell, projected with Unity's own camera
        private bool _pillar;
        private int _pillarStage;
        private int _pillarX, _pillarY, _pillarZ;
        private const int PillarHeight = 3;
        private StreamWriter _projLog;

        private void OnEnable()
        {
            Camera.onPreRender += HandlePreRender;
            SteveAvatar.Enable();
        }

        private void OnDisable()
        {
            Camera.onPreRender -= HandlePreRender;
            SteveAvatar.Disable();
            _projLog?.Dispose();
            _projLog = null;
        }

        /// <summary>Valheim's camera distance behind the player (0 = first person); for the test status line.</summary>
        internal static float CameraDistance()
        {
            return GameCamera.instance != null && DistanceField != null ? (float)DistanceField.GetValue(GameCamera.instance) : -1f;
        }

        // Steve stands where the Viking stands: in Minecraft mode the Viking's body is hidden (re-checked twice a
        // second, since equipment and armour swap renderers). Only skinned meshes: held items and their lights stay.
        private readonly System.Collections.Generic.List<SkinnedMeshRenderer> _hidden = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        internal static int VikingHidden; // renderers hidden right now (status5)
        private float _nextVikingCheck;

        private Hud _hotbarHud;
        private HotkeyBar[] _hotbars;
        private bool _hotbarsHidden;

        /// <summary>
        /// In Minecraft mode Steve's hotbar is Minecraft's: Valheim's own (HotkeyBar) is hidden, health, stamina and food stay.
        /// The bars are switched off rather than faded so their number keys are not drawn as Valheim's either.
        /// </summary>
        private void UpdateValheimHotbar(Player player)
        {
            bool hide = player != null && Plugin.HideValheimHotbar.Value && Link.Connected && InputBridge.McMode;
            if (Hud.instance != _hotbarHud)
            {
                _hotbarHud = Hud.instance;
                _hotbars = _hotbarHud != null ? _hotbarHud.GetComponentsInChildren<HotkeyBar>(true) : null;
                _hotbarsHidden = false;
            }

            if (_hotbars == null || hide == _hotbarsHidden)
            {
                return;
            }

            _hotbarsHidden = hide;
            foreach (HotkeyBar bar in _hotbars)
            {
                if (bar != null)
                {
                    bar.gameObject.SetActive(!hide);
                }
            }
        }

        private void UpdateVikingVisibility(Player player)
        {
            if (Time.unscaledTime < _nextVikingCheck)
            {
                return;
            }

            _nextVikingCheck = Time.unscaledTime + 0.5f;
            bool hide = player != null && Plugin.HideViking.Value && Link.Connected && InputBridge.McMode;
            VikingHidden = hide ? _hidden.Count : 0;
            if (hide)
            {
                GameObject visual = player.GetVisual();
                if (visual == null)
                {
                    return;
                }

                foreach (SkinnedMeshRenderer r in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                        _hidden.Add(r);
                    }
                }
            }
            else if (_hidden.Count > 0)
            {
                foreach (SkinnedMeshRenderer r in _hidden)
                {
                    if (r != null)
                    {
                        r.enabled = true;
                    }
                }

                _hidden.Clear();
            }
        }

        private bool _hadPlayer;
        private readonly System.Collections.Generic.List<int> _heights = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _barrierAdd = new System.Collections.Generic.List<int>(), _barrierRemove = new System.Collections.Generic.List<int>();
        private Vector3 _lastSyncPos;
        private float _lastSyncTime;

        private void Dispatch(string message)
        {
            switch (Messages.Type(message))
            {
                case "blocks":
                    Plugin.Blocks.Enqueue(message);
                    return;
                case "probe":
                    Plugin.Hooks.OnProbeReply(message);
                    return;
                case "mclights":
                    if (Plugin.MinecraftLamps.Value)
                    {
                        Plugin.Lights.Enqueue(message, Plugin.YOffset.Value);
                    }

                    return;
                case "screen":
                    ScreenBridge.OnScreen(message);
                    return;
                case "skin":
                    SteveAvatar.OnSkin(message);
                    return;
                case "armswing":
                    SteveAvatar.OnArmSwing();
                    return;
                case "infect":
                    Plugin.Nether.OnInfect(message);
                    return;
                case "itemout":
                    Plugin.Items.OnOffer(message);
                    return;
                case "itemtaken":
                    Plugin.Items.OnTaken(message);
                    return;
                case "query":
                    Plugin.Log.LogInfo("minecraft query: " + message);
                    return;
                case "selected":
                    InputBridge.McSlot = Messages.IntValue(message, "n", -1);
                    Plugin.Log.LogInfo("minecraft hotbar slot -> " + InputBridge.McSlot);
                    return;
                case "hello":
                case "ready":
                    Plugin.ResetWorldState("minecraft sent " + Messages.Type(message));
                    return;
            }

            if (Plugin.Events.Handle(Messages.Type(message), message))
            {
                return;
            }

            if (_received++ < 20)
            {
                Plugin.Log.LogInfo("minecraft: " + (message.Length > 200 ? message.Substring(0, 200) + "..." : message));
            }
        }

        /// <summary>Valheim's frame rate over the last 5 s (the status key).</summary>
        internal static float Fps;
        private float _perfStart;
        private int _perfFrames;
        private long _perfCamStart;

        private void Update()
        {
            // one status line every 5 s while a player exists: frame rate, cams sent, link state (helps on weak PCs)
            _perfFrames++;
            if (Time.unscaledTime - _perfStart >= 5f)
            {
                if (Player.m_localPlayer != null)
                {
                    float offset = MotionPrediction.TakeOffset();
                    Fps = _perfFrames / (Time.unscaledTime - _perfStart);
                    Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "perf: {0:0.0} fps, {1} cam sent, link {2}, window {3}x{4}, minecraft {5:0.0} frame(s) behind, steve lead {6:0} ms, steve offset {7}, steve drawn by {8}",
                        _perfFrames / (Time.unscaledTime - _perfStart), _frame - _perfCamStart, Link.Connected ? "up" : "down", _viewW, _viewH,
                        MotionPrediction.LagFrames, MotionPrediction.Lead * 1000f, offset < 0f ? "n/a" : (offset * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " cm",
                        SteveAvatar.Native ? "valheim (skin: " + SteveAvatar.SkinSource + ")" : "minecraft"));
                }

                _perfStart = Time.unscaledTime;
                _perfFrames = 0;
                _perfCamStart = _frame;
            }

            bool connected = Link.Connected;
            if (connected && !_wasConnected)
            {
                _viewW = _viewH = 0; // (re)send the window size to a fresh Minecraft
                if (_pillar)
                {
                    PlacePillarInMinecraft();
                }
            }

            if (!connected && _wasConnected)
            {
                ShieldBlock.Up = false; // a shield raised in a Minecraft that went away is not raised any more
                ScreenBridge.Disconnected();
            }

            _wasConnected = connected;

            string message;
            while (Link.TryDequeue(out message))
            {
                Dispatch(message);
            }

            Player player = Player.m_localPlayer;
            double yOffset = Plugin.YOffset.Value;
            if (player != null && !_hadPlayer)
            {
                Plugin.ResetWorldState("player entered a world");
                _lastSyncPos = player.transform.position;
                _lastSyncTime = Time.unscaledTime;
            }

            _hadPlayer = player != null;
            if (player != null && Link.Connected)
            {
                if (Plugin.GroundEnabled.Value)
                {
                    Plugin.Ground.Tick(player.transform.position, yOffset, Plugin.GroundRadius.Value, Plugin.GroundPerFrame.Value);
                }

                if (Plugin.BuildingsEnabled.Value)
                {
                    Plugin.Pieces.Tick(player.transform.position, yOffset, Plugin.BuildingRadius.Value, Plugin.BuildingTestsPerFrame.Value, Plugin.BuildingMaxCells.Value);
                }

                // ground and buildings share the barrier cells: one message a frame with what changed
                if (Plugin.Cells.Drain(_barrierAdd, _barrierRemove))
                {
                    Link.Send(Messages.Ground(_barrierAdd, _barrierRemove));
                    _barrierAdd.Clear();
                    _barrierRemove.Clear();
                }

                if (Plugin.Ground.DrainHeights(_heights))
                {
                    Link.Send("{\"t\":\"heights\",\"c\":[" + string.Join(",", _heights) + "]}");
                    _heights.Clear();
                }

                // Minecraft's world persists, Valheim's colliders do not: refresh them when the player has moved on
                if ((player.transform.position - _lastSyncPos).sqrMagnitude > 24f * 24f && Time.unscaledTime - _lastSyncTime > 10f)
                {
                    _lastSyncPos = player.transform.position;
                    _lastSyncTime = Time.unscaledTime;
                    Plugin.RequestBlockSync();
                }
            }

            InputBridge.Poll();
            ScreenBridge.Poll();
            MinecraftLauncher.Tick();
            UpdateVikingVisibility(player);
            UpdateValheimHotbar(player);
            Flight.Tick(player);
            Plugin.Lights.Tick(player, yOffset);
            Plugin.Mobs.Tick(player, yOffset);
            VikingStats.Tick(player);
            Plugin.Nether.Tick(player);
            Plugin.Blocks.Tick(yOffset, Plugin.MaxColliders.Value);
            Plugin.Hooks.Tick(player, yOffset);

            // the game reads keys through Unity's new Input System; legacy UnityEngine.Input may be disabled
            if (ZInput.GetKeyDown(Plugin.PillarKey.Value, false))
            {
                // F9 cycles: off -> pillar 5 m past the player -> 12 m -> off (the far one tests occlusion by Valheim's world)
                if (_pillar)
                {
                    Link.Send(Messages.Command("fill " + _pillarX + " " + _pillarY + " " + _pillarZ + " " + _pillarX + " " + (_pillarY + PillarHeight - 1) + " " + _pillarZ + " minecraft:air"));
                }

                _pillarStage = (_pillarStage + 1) % 3;
                _pillar = _pillarStage != 0;
                if (_pillar)
                {
                    ChoosePillar(_pillarStage == 1 ? 5f : 12f);
                }
                else
                {
                    Plugin.Log.LogInfo("pillar test off");
                }
            }
        }

        // not named OnPreRender: Unity treats that as a message method and objects to the parameter
        private void HandlePreRender(Camera cam)
        {
            var gameCamera = GameCamera.instance;
            if (gameCamera == null || !Plugin.Enabled.Value)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = gameCamera.GetComponent<Camera>();
            }

            Player player = Player.m_localPlayer;
            if (cam != _camera || player == null)
            {
                return;
            }

            int w = cam.pixelWidth, h = cam.pixelHeight;
            if (Link.Connected && (w != _viewW || h != _viewH))
            {
                _viewW = w;
                _viewH = h;
                Link.Send(Messages.View(w, h));
            }

            double yOffset = Plugin.YOffset.Value;
            Transform t = cam.transform;
            Vector3 p = t.position, f = t.forward, u = t.up;
            double mx, my, mz, yaw, pitch, roll;
            Mapping.Position(p.x, p.y, p.z, yOffset, out mx, out my, out mz);
            Mapping.Orientation(f.x, f.y, f.z, u.x, u.y, u.z, out yaw, out pitch, out roll);

            Vector3 body = player.transform.forward;
            Vector3 feet = MotionPrediction.Feet(_frame + 1, player.transform.position);
            if (player.IsSwimming() && player.m_eye != null)
            {
                // a swimming Viking keeps his head above the water and his feet far below it: Steve's eyes go where his are
                // (from the feet, Steve's eyes would be under the surface and Valheim's water would hide all of him)
                feet.y = player.m_eye.position.y - 1.62f;
            }
            double fx, fy, fz;
            Mapping.Position(feet.x, feet.y, feet.z, yOffset, out fx, out fy, out fz);
            bool firstPerson = DistanceField != null && (float)DistanceField.GetValue(gameCamera) <= 0.01f;

            _frame++;
            // for the compositor: this frame's pose, and a marker that tells it (on the render thread) which frame is being drawn
            // flags: 1 in a world, 2 first person, 4 a Valheim menu is open (the compositor steps aside: Minecraft's picture would
            // cover it), 8 Minecraft is attached
            bool valheimUi = Cursor.lockState != CursorLockMode.Locked && !ScreenBridge.Open;
            HostPoseShm.Write(_frame, mx, my, mz, (float)yaw, (float)pitch, (float)roll, cam.fieldOfView, cam.nearClipPlane, cam.farClipPlane,
                1 | (firstPerson ? 2 : 0) | (valheimUi ? 4 : 0) | (Link.Connected ? 8 : 0), System.Diagnostics.Stopwatch.GetTimestamp(),
                NetherBridge.Heat, NetherBridge.EndHere);
            UpdateMarker(cam, _frame);
            if (Link.Connected)
            {
                Link.SendLatest(Messages.Cam(_frame, mx, my, mz, yaw, pitch, roll, cam.fieldOfView, firstPerson, fx, fy, fz, Mapping.Yaw(body.x, body.z)));
            }

            if (_pillar)
            {
                LogProjection(cam, yOffset, w, h);
            }
        }

        /// <summary>
        /// Valheim renders on its own thread, one frame behind the main thread, so the newest pose is not necessarily the
        /// one on screen. A plugin event queued in the main camera's command stream runs on the render thread exactly when
        /// that camera is drawn: the ReShade add-on (MineHeim.addon64, same process) records the frame number it
        /// carries and later looks up that frame's pose.
        /// </summary>
        private void UpdateMarker(Camera cam, long frame)
        {
            if (_renderEvent == IntPtr.Zero)
            {
                if (Time.unscaledTime < _nextLookup)
                {
                    return;
                }

                _nextLookup = Time.unscaledTime + 2f;
                IntPtr module = GetModuleHandleW("MineHeim.addon64");
                IntPtr proc = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "VCRenderEvent");
                if (proc == IntPtr.Zero)
                {
                    return; // ReShade or the add-on is not installed: nothing to synchronise with
                }

                _renderEvent = proc;
                _marker = new CommandBuffer { name = "mineheim frame marker" };
                // which events run depends on the camera's rendering path; the last one to run before Present wins,
                // so attach to several (the add-on counts how often the marker fires)
                foreach (CameraEvent e in new[] { CameraEvent.BeforeForwardOpaque, CameraEvent.BeforeGBuffer, CameraEvent.BeforeDepthTexture,
                                                  CameraEvent.BeforeImageEffects, CameraEvent.AfterEverything })
                {
                    cam.AddCommandBuffer(e, _marker);
                }

                Plugin.Log.LogInfo("render marker attached to the main camera (rendering path " + cam.actualRenderingPath + ")");
            }

            _marker.Clear();
            _marker.IssuePluginEvent(_renderEvent, (int)(frame & 0x7fffffff));
        }

        // ------------------------------------------------------------------ alignment test pillar

        private void ChoosePillar(float distance)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _pillar = false;
                return;
            }

            // 5 m past the player along the camera's horizontal direction (so it is in front of the camera, not behind it),
            // on the Minecraft block grid, standing on the Minecraft cell that holds the feet
            Vector3 feet = player.transform.position;
            Vector3 flat = (_camera != null ? _camera.transform.forward : player.transform.forward);
            flat.y = 0f;
            flat.Normalize();
            Vector3 q = feet + flat * distance;
            _pillarX = Mathf.FloorToInt(q.x);
            _pillarZ = Mathf.FloorToInt(-q.z);
            _pillarY = Mathf.FloorToInt(feet.y + (float)Plugin.YOffset.Value);
            Plugin.Log.LogInfo("pillar test on: Minecraft cells x=" + _pillarX + " y=" + _pillarY + ".." + (_pillarY + PillarHeight - 1) + " z=" + _pillarZ);
            PlacePillarInMinecraft();
            string path = Path.Combine(Paths.BepInExRootPath, "mineheim-proj.csv");
            _projLog?.Dispose();
            _projLog = new StreamWriter(path, false) { AutoFlush = true };
            _projLog.WriteLine("frame,x0,y0,x1,y1,width,height,cellx,celly,cellz,yoffset");
        }

        private void PlacePillarInMinecraft()
        {
            Link.Send(Messages.Command("fill " + _pillarX + " " + _pillarY + " " + _pillarZ + " " + _pillarX + " " + (_pillarY + PillarHeight - 1) + " " + _pillarZ + " minecraft:gold_block"));
        }

        /// <summary>
        /// Unity's own projection of the pillar's 8 corners with the camera that is about to render, as a pixel box
        /// (origin bottom-left). Written for the frame number sent to Minecraft, which echoes it in its exported frame.
        /// </summary>
        private void LogProjection(Camera cam, double yOffset, int w, int h)
        {
            if (_projLog == null)
            {
                return;
            }

            // the cell [x, x+1] x [y, y+height] x [z, z+1] in Minecraft, back in Valheim axes (z mirrored, y without the offset)
            float x0 = _pillarX, x1 = _pillarX + 1;
            float z0 = -(_pillarZ + 1), z1 = -_pillarZ;
            float y0 = (float)(_pillarY - yOffset), y1 = (float)(_pillarY + PillarHeight - yOffset);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = cam.WorldToScreenPoint(new Vector3((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
                if (s.z <= cam.nearClipPlane)
                {
                    return; // a corner is behind the camera: no clean box
                }

                minX = Mathf.Min(minX, s.x);
                maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y);
                maxY = Mathf.Max(maxY, s.y);
            }

            _projLog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1:0.###},{2:0.###},{3:0.###},{4:0.###},{5},{6},{7},{8},{9},{10}",
                _frame, minX, minY, maxX, maxY, w, h, _pillarX, _pillarY, _pillarZ, yOffset));
        }
    }
}
