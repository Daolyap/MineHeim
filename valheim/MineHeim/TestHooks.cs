using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Hooks for automated tests (no window focus or key presses needed): the plugin polls
    /// BepInEx/mineheim-cmd.txt twice a second, runs each line and deletes the file.
    ///   playerlog on|off     write BepInEx/mineheim-player.csv (10 Hz: time, Valheim position, Minecraft position)
    ///   groundcheck [n]      check n ground columns near the player against Minecraft's barrier blocks (result in the log)
    ///   status               log collider / ground / link counts
    ///   blocksync            ask Minecraft for the solid blocks around the player
    ///   clearblocks          remove every block collider
    /// </summary>
    internal sealed class TestHooks
    {
        private readonly string _cmdPath = Path.Combine(Paths.BepInExRootPath, "mineheim-cmd.txt");
        private readonly string _playerPath = Path.Combine(Paths.BepInExRootPath, "mineheim-player.csv");
        private StreamWriter _playerLog;
        private float _nextPoll, _nextSample;

        // a groundcheck in flight: what each probed cell should hold, and how the columns compare with a fresh raycast
        private List<int> _expected;
        private int _columns, _freshMatches;

        // messages to Minecraft at set times ("mcattack": clicks closer together than a command file can be polled)
        private readonly List<KeyValuePair<float, string>> _timed = new List<KeyValuePair<float, string>>();

        public void Tick(Player player, double yOffset)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= _timed[i].Key)
                {
                    Link.Send(_timed[i].Value);
                    _timed.RemoveAt(i);
                }
            }

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.5f;
                PollCommands(player, yOffset);
            }

            if (_playerLog != null && player != null && Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime + 0.1f;
                Vector3 p = player.transform.position;
                double mx, my, mz;
                Mapping.Position(p.x, p.y, p.z, yOffset, out mx, out my, out mz);
                _playerLog.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000},{3:0.000},{4:0.000},{5:0.000},{6:0.000}",
                    Time.unscaledTime, p.x, p.y, p.z, mx, my, mz));
            }
        }

        private void PollCommands(Player player, double yOffset)
        {
            if (!File.Exists(_cmdPath))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_cmdPath);
                File.Delete(_cmdPath);
            }
            catch (IOException)
            {
                return; // being written: next poll
            }

            foreach (string raw in lines)
            {
                string[] w = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0)
                {
                    continue;
                }

                Plugin.Log.LogInfo("test command: " + raw.Trim());
                switch (w[0])
                {
                    case "playerlog":
                        _playerLog?.Dispose();
                        _playerLog = null;
                        if (w.Length > 1 && w[1] == "on")
                        {
                            _playerLog = new StreamWriter(_playerPath, false) { AutoFlush = true };
                            _playerLog.WriteLine("t,x,y,z,mcx,mcy,mcz");
                        }

                        break;
                    case "groundcheck":
                        GroundCheck(player, yOffset, w.Length > 1 ? int.Parse(w[1], CultureInfo.InvariantCulture) : 24);
                        break;
                    case "status":
                        Plugin.Log.LogInfo(string.Format("status: colliders={0} pending={1} groundColumns={2} link={3} mcMode={4} valheimAttacks={5} hotbarUses={6} mcSlot={7} camDistance={8:0.00} explosions={9} swings={10} projHits={11} toolHits={12}",
                            Plugin.Blocks.Count, Plugin.Blocks.Pending, Plugin.Ground.ColumnsSent, Link.Connected ? "up" : "down",
                            InputBridge.McMode ? "on" : "off", InputBridge.ValheimAttacks, InputBridge.HotbarUses, InputBridge.McSlot, PoseSender.CameraDistance(),
                            Plugin.Events.Explosions, Plugin.Events.Swings, Plugin.Events.ProjectileHits, Plugin.Events.ToolHits));
                        Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "status2: flying={0} shieldUp={1} shieldBlocks={2} structureHits={3} craters={4} digs={5} mobHits={6} gameMode={7} health={8:0.0} y={9:0.00}",
                            Flight.Active ? "on" : "off", ShieldBlock.Up ? "yes" : "no", ShieldBlock.Blocks, Plugin.Events.StructureHits, Plugin.Events.Craters, Plugin.Events.Digs,
                            Plugin.Events.MobHits, Plugin.GameMode.Value, player != null ? player.GetHealth() : 0f, player != null ? player.transform.position.y : 0f));
                        break;
                    case "status3":
                        Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "status3: pieces={0} voxelized={1} pieceCells={2} barrierCells={3} pendingCells={4} fires={5} lamps={6} lampsLit={7} itemsToMc={8} itemsToValheim={9} offers={10} mcTicks={11} dayFraction={12:0.000}",
                            Plugin.Pieces.Pieces, Plugin.Pieces.Voxelized, Plugin.Pieces.CellCount, Plugin.Cells.Count, Plugin.Cells.PendingCount, Plugin.Lights.FiresSent,
                            Plugin.Lights.Lamps, Plugin.Lights.LampsLit, Plugin.Items.ToMinecraft, Plugin.Items.ToValheim, Plugin.Items.Offers, Plugin.Lights.Ticks,
                            EnvMan.instance != null ? EnvMan.instance.GetDayFraction() : -1f));
                        break;
                    case "status4":
                        Character body = null;
                        foreach (Character c in Character.GetAllCharacters())
                        {
                            if (MobBridge.IsBody(c) && (body == null || (c.transform.position - player.transform.position).sqrMagnitude < (body.transform.position - player.transform.position).sqrMagnitude))
                            {
                                body = c;
                            }
                        }

                        Character creature = _spawned.Count > 0 ? _spawned[0] : null;
                        Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "status4: creaturesSent={0} bodies={1} bodiesMade={2} hitsToMinecraft={3} hitsFromMinecraft={4} rate={5:0.00} bodyHp={6:0.0} creatureHp={7:0.0}",
                            Plugin.Mobs.CreaturesSent, Plugin.Mobs.BodyCount, Plugin.Mobs.BodiesMade, Plugin.Mobs.HitsToMinecraft, Plugin.Mobs.HitsFromMinecraft, MobBridge.Rate(),
                            body != null ? body.GetHealth() : -1f, creature != null ? (creature.IsDead() ? 0f : creature.GetHealth()) : -1f));
                        break;
                    case "mcmob":
                        // a Minecraft mob `ahead` metres in front of the camera, on the ground: "mcmob zombie 6"
                        McMob(player, yOffset, w.Length > 1 ? w[1] : "zombie", w.Length > 2 ? float.Parse(w[2], CultureInfo.InvariantCulture) : 6f);
                        break;
                    case "mcbuild":
                        // a small Minecraft cottage `ahead` metres in front of the camera (for screenshots and takes)
                        McBuild(player, yOffset, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 9f);
                        break;
                    case "hitbody":
                        // the Viking hits the nearest Minecraft mob's body in Valheim for n damage (as with a Valheim weapon)
                        HitBody(player, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 20f);
                        break;
                    case "drop":
                        // the Viking drops n of an item (given to him first), exactly as from his inventory
                        DropItem(player, w.Length > 1 ? w[1] : "Wood", w.Length > 2 ? int.Parse(w[2], CultureInfo.InvariantCulture) : 1);
                        break;
                    case "count":
                        Plugin.Log.LogInfo(string.Format("count: {0} x{1}", w.Length > 1 ? w[1] : "Wood", CountItem(player, w.Length > 1 ? w[1] : "Wood")));
                        break;
                    case "query":
                        Link.Send("{\"t\":\"query\"}");
                        break;
                    case "voxelcheck":
                        VoxelCheck(yOffset);
                        break;
                    case "boom":
                        Boom(player, yOffset, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 6f);
                        break;
                    case "terrain":
                        TerrainHeight(player, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 3f);
                        break;
                    case "recover":
                        // the Viking's own tombstones in the loaded area: Valheim's own "take all" (it checks the owner itself)
                        foreach (TombStone stone in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
                        {
                            bool taken = player != null && stone.Interact(player, false, false);
                            Plugin.Log.LogInfo(string.Format("tombstone of {0} at {1}: {2}", stone.GetOwnerName(), stone.transform.position, taken ? "recovered" : "not taken"));
                        }

                        break;
                    case "status5":
                        Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                            "status5: mcMode={0} vikingHidden={1} creativeBlocked={2} wardSaved={3} cursorVisible={4} cursorLock={5} health={6:0.0} stamina={7:0.0} swings={8} lagFrames={9:0.0} leadMs={10:0} predict={11} screen={12} x={13:0.00} z={14:0.00}",
                            InputBridge.McMode ? "on" : "off", PoseSender.VikingHidden, CreativeViking.Blocked, Plugin.Events.WardSaved, ZCursor.IsVisible ? "yes" : "no",
                            Cursor.lockState, player != null ? player.GetHealth() : 0f, player != null ? player.GetStamina() : 0f, Plugin.Events.Swings,
                            MotionPrediction.LagFrames, MotionPrediction.Lead * 1000f, Plugin.PredictMotion.Value ? "on" : "off", ScreenBridge.Open ? ScreenBridge.Name : "none",
                            player != null ? player.transform.position.x : 0f, player != null ? player.transform.position.z : 0f));
                        break;
                    case "nether":
                        {
                            var parts = new List<string>();
                            int cells = 0;
                            foreach (NetherBridge.Zone z in NetherBridge.Zones.Values)
                            {
                                cells += z.Cells;
                                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.0}:{2}:{3}:{4:0.0}:{5}:{6}", z.Id, z.Radius, z.Lit ? "lit" : "healing", z.Columns,
                                    player != null ? Utils.DistanceXZ(player.transform.position, z.Centre) : -1f, z.Kind == 1 ? "end" : "nether", z.Dragon ? "dragon" : "-"));
                            }

                            Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                                "nether: zones={0} list={1} mobs={2} recolored={3} painted={4} env={5} reports={6} weightHere={7:0.00} cells={8} heat={9:0.00} endHere={10:0.00} burned={11} fled={12} warped={13}",
                                NetherBridge.Zones.Count, parts.Count == 0 ? "-" : string.Join(",", parts.ToArray()), NetherBridge.Mobs, NetherBridge.Recolored,
                                NetherBridge.PaintedNow, NetherBridge.Env.Length == 0 ? "-" : NetherBridge.Env, NetherBridge.Reports,
                                player != null ? NetherBridge.Weight(player.transform.position) : 0f, cells, NetherBridge.Heat, NetherBridge.EndHere,
                                NetherBridge.Burned, NetherBridge.Fled, NetherBridge.Warped));
                        }

                        break;
                    case "nethercmd":
                        // clear | heal | grow, straight to Minecraft's infection
                        Link.Send("{\"t\":\"nether\",\"a\":\"" + (w.Length > 1 ? w[1] : "clear") + "\"}");
                        break;
                    case "mcraw":
                        // a raw message to Minecraft: mcraw {"t":"nethercfg","grow":120}
                        Link.Send(raw.Trim().Substring(5).Trim());
                        break;
                    case "envs":
                        Plugin.Log.LogInfo("environments: " + NetherBridge.EnvNames());
                        break;
                    case "creativegod":
                        // older tests measure damage in Creative: "creativegod off" lets it land again (not saved to the config)
                        CreativeViking.TestOff = w.Length > 1 && w[1] == "off";
                        Plugin.Log.LogInfo("creative invulnerability " + (CreativeViking.TestOff ? "off for tests" : "as configured"));
                        break;
                    case "predict":
                        Plugin.PredictMotion.Value = !(w.Length > 1 && w[1] == "off");
                        break;
                    case "stamina":
                        // use this much stamina, as a swing or a sprint would
                        player?.UseStamina(w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 20f);
                        break;
                    case "move":
                        // teleport the Viking dx, dz metres (Valheim axes), onto the ground there: "move 30 0"
                        if (player != null)
                        {
                            Vector3 to = player.transform.position + new Vector3(w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 20f, 0f,
                                                                                   w.Length > 2 ? float.Parse(w[2], CultureInfo.InvariantCulture) : 0f);
                            RaycastHit down;
                            if (Physics.Raycast(to + Vector3.up * 200f, Vector3.down, out down, 400f, LayerMask.GetMask("terrain")))
                            {
                                to.y = down.point.y + 0.3f;
                            }

                            player.TeleportTo(to, player.transform.rotation, false);
                            Plugin.Log.LogInfo("moved to " + to);
                        }

                        break;
                    case "turn":
                        // turn the Viking (and the camera) by this many degrees
                        player?.SetMouseLook(new Vector2(w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 90f, 0f));
                        break;
                    case "autorun":
                        if (player != null)
                        {
                            player.m_autoRun = w.Length > 1 && w[1] == "on";
                        }

                        break;
                    case "mccmd":
                        Link.Send(Messages.Command(raw.Trim().Substring(5).Trim()));
                        break;
                    case "mcattack":
                        // n attack clicks gapMs apart, as Minecraft mode forwards them: "mcattack 3 150"
                        {
                            int n = w.Length > 1 ? int.Parse(w[1], CultureInfo.InvariantCulture) : 2;
                            float gap = (w.Length > 2 ? float.Parse(w[2], CultureInfo.InvariantCulture) : 150f) / 1000f;
                            for (int i = 0; i < n; i++)
                            {
                                _timed.Add(new KeyValuePair<float, string>(Time.unscaledTime + i * gap, "{\"t\":\"key\",\"k\":\"attack\",\"down\":true}"));
                                _timed.Add(new KeyValuePair<float, string>(Time.unscaledTime + i * gap + 0.03f, "{\"t\":\"key\",\"k\":\"attack\",\"down\":false}"));
                            }
                        }

                        break;
                    case "wardon":
                        // the wards spawned with "spawn guard_stone <m>": switched on, as when a player places one
                        foreach (GameObject g in _props)
                        {
                            PrivateArea ward = g != null ? g.GetComponent<PrivateArea>() : null;
                            if (ward != null)
                            {
                                ward.Setup(Game.instance.GetPlayerProfile().GetName());
                                HarmonyLib.AccessTools.Method(typeof(PrivateArea), "SetEnabled").Invoke(ward, new object[] { true });
                                Plugin.Log.LogInfo("ward on at " + g.transform.position + ": inside=" + WorldEvents.InWard(g.transform.position));
                            }
                        }

                        break;
                    case "nodeath":
                        NoDeath = w.Length > 1 && w[1] == "on";
                        Plugin.Log.LogInfo("test no-death guard " + (NoDeath ? "on" : "off"));
                        break;
                    case "fly":
                        if (w.Length > 1 && w[1] == "on")
                        {
                            Flight.Start();
                        }
                        else
                        {
                            Flight.Stop("test command");
                        }

                        break;
                    case "shield":
                        ShieldBlock.Up = w.Length > 1 && w[1] == "on"; // as if Minecraft reported the shield raised
                        break;
                    case "hurt":
                        // as if a Minecraft mob 2 m in front of the camera hit Steve for that much Minecraft damage
                        SimulateMobHit(player, yOffset, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 3f);
                        break;
                    case "gamemode":
                        Plugin.GameMode.Value = w.Length > 1 && w[1] == "survival" ? Plugin.SteveMode.Survival : Plugin.SteveMode.Creative;
                        break;
                    case "heal":
                        if (player != null)
                        {
                            player.Heal(player.GetMaxHealth(), false);
                        }

                        break;
                    case "spawn":
                        Spawn(player, w.Length > 1 ? w[1] : "Greydwarf", w.Length > 2 ? float.Parse(w[2], CultureInfo.InvariantCulture) : 6f, w.Length > 3 && w[3] == "ai");
                        break;
                    case "creatures":
                        ReportCreatures(yOffset);
                        break;
                    case "cleanup":
                        foreach (Character c in _spawned)
                        {
                            if (c != null && ZNetScene.instance != null)
                            {
                                ZNetScene.instance.Destroy(c.gameObject);
                            }
                        }

                        _spawned.Clear();
                        foreach (GameObject g in _props)
                        {
                            if (g != null && ZNetScene.instance != null)
                            {
                                ZNetScene.instance.Destroy(g);
                            }
                        }

                        _props.Clear();
                        break;
                    case "props":
                        Plugin.Log.LogInfo("props alive: " + _props.FindAll(g => g != null).Count + " of " + _props.Count);
                        break;
                    case "tnt":
                        Tnt(yOffset, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 1.5f);
                        break;
                    case "inputmode":
                        InputBridge.SetMode(w.Length > 1 && w[1] == "on", "test command");
                        break;
                    case "blocksync":
                        Plugin.RequestBlockSync();
                        break;
                    case "clearblocks":
                        Plugin.Blocks.Clear();
                        break;
                    case "pitch":
                        SetPitch(player, w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : 0f);
                        break;
                    case "time":
                        SetTimeOfDay(w.Length > 1 ? float.Parse(w[1], CultureInfo.InvariantCulture) : -1f);
                        break;
                    default:
                        Plugin.Log.LogWarning("unknown test command: " + raw.Trim());
                        break;
                }
            }
        }

        /// <summary>The ground point head metres in front of the camera (terrain only), if any.</summary>
        private static bool GroundAhead(Player player, float ahead, out Vector3 point)
        {
            point = Vector3.zero;
            if (player == null || GameCamera.instance == null)
            {
                return false;
            }

            Vector3 fwd = GameCamera.instance.transform.forward;
            fwd.y = 0f;
            Vector3 p = player.transform.position + fwd.normalized * ahead;
            RaycastHit ground;
            if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out ground, 80f, LayerMask.GetMask("terrain")))
            {
                return false;
            }

            point = ground.point;
            return true;
        }

        /// <summary>Minecraft TNT on the ground head metres in front of the camera (a short fuse).</summary>
        private static void Boom(Player player, double yOffset, float ahead)
        {
            Vector3 at;
            if (!GroundAhead(player, ahead, out at))
            {
                Plugin.Log.LogWarning("boom: no ground ahead");
                return;
            }

            double mx, my, mz;
            Mapping.Position(at.x, at.y + 0.3, at.z, yOffset, out mx, out my, out mz);
            Link.Send(Messages.Command(string.Format(CultureInfo.InvariantCulture, "summon minecraft:tnt {0:0.###} {1:0.###} {2:0.###} {{fuse:10}}", mx, my, mz)));
            Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "boom: TNT at {0} (terrain y {1:0.000})", at, at.y));
        }

        private static void TerrainHeight(Player player, float ahead)
        {
            Vector3 at;
            if (GroundAhead(player, ahead, out at))
            {
                Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "terrain: y={0:0.000} at {1:0.00},{2:0.00}", at.y, at.x, at.z));
            }
            else
            {
                Plugin.Log.LogWarning("terrain: no ground ahead");
            }
        }

        /// <summary>Tests only ("nodeath on"): the Viking's health never drops below 1, whatever hits him (natural spawns included).</summary>
        public static bool NoDeath;

        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class NoDeathPatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!NoDeath || __instance != Player.m_localPlayer)
                {
                    return;
                }

                float total = hit.GetTotalDamage(), health = __instance.GetHealth();
                if (total >= health - 1f && total > 0f)
                {
                    hit.ApplyModifier(Mathf.Max(0f, (health - 1.5f) / total));
                    Plugin.Log.LogInfo(string.Format("test no-death guard: a {0:0.0} hit cut to leave the Viking alive", total));
                }
            }
        }

        private static void SimulateMobHit(Player player, double yOffset, float amount)
        {
            if (player == null || GameCamera.instance == null)
            {
                return;
            }

            Vector3 fwd = GameCamera.instance.transform.forward;
            fwd.y = 0f;
            Vector3 from = player.transform.position + fwd.normalized * 2f + Vector3.up;
            double mx, my, mz;
            Mapping.Position(from.x, from.y, from.z, yOffset, out mx, out my, out mz);
            Plugin.Events.Handle("hurt", string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"hurt\",\"amount\":{0},\"src\":\"test_zombie\",\"pos\":[{1},{2},{3}]}}", amount, mx, my, mz));
        }

        /// <summary>Look `degrees` down (negative: up). Mouse counts are ~0.05 degrees each, too coarse to aim a take by hand.</summary>
        private static void SetPitch(Player player, float degrees)
        {
            System.Reflection.FieldInfo f = HarmonyLib.AccessTools.Field(typeof(Player), "m_lookPitch");
            if (player == null || f == null)
            {
                Plugin.Log.LogWarning("pitch: no player or no m_lookPitch");
                return;
            }

            f.SetValue(player, degrees);
            Plugin.Log.LogInfo("pitch set to " + degrees);
        }

        /// <summary>Freeze the sky at `fraction` of the day (0 midnight, 0.5 noon); a negative value hands the clock back to the game.</summary>
        private static void SetTimeOfDay(float fraction)
        {
            if (EnvMan.instance == null)
            {
                Plugin.Log.LogWarning("time: no EnvMan");
                return;
            }

            System.Reflection.FieldInfo on = HarmonyLib.AccessTools.Field(typeof(EnvMan), "m_debugTimeOfDay");
            System.Reflection.FieldInfo at = HarmonyLib.AccessTools.Field(typeof(EnvMan), "m_debugTime");
            if (on == null || at == null)
            {
                Plugin.Log.LogWarning("time: EnvMan has no debug time fields");
                return;
            }

            on.SetValue(EnvMan.instance, fraction >= 0f);
            at.SetValue(EnvMan.instance, Mathf.Clamp01(fraction));
            Plugin.Log.LogInfo("time of day " + (fraction >= 0f ? "frozen at " + fraction : "released"));
        }

        // ------------------------------------------------------------------ creatures for the event tests

        private readonly List<Character> _spawned = new List<Character>();
        private readonly List<GameObject> _props = new List<GameObject>();

        /// <summary>A creature standing still (its AI is switched off) `ahead` metres in front of the camera, to be hurt by Minecraft.</summary>
        private void Spawn(Player player, string prefabName, float ahead, bool keepAi = false)
        {
            if (player == null || ZNetScene.instance == null)
            {
                Plugin.Log.LogWarning("spawn: needs a player in a world");
                return;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning("spawn: no prefab named " + prefabName);
                return;
            }

            Vector3 fwd = GameCamera.instance != null ? GameCamera.instance.transform.forward : player.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 pos = player.transform.position + fwd * ahead;
            RaycastHit ground;
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out ground, 60f, LayerMask.GetMask("Default", "static_solid", "terrain")))
            {
                pos.y = ground.point.y + 0.2f;
            }

            GameObject go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd));
            foreach (BaseAI ai in go.GetComponentsInChildren<BaseAI>())
            {
                ai.enabled = keepAi; // "spawn <prefab> <m> ai": a creature that really fights
            }

            Character c = go.GetComponent<Character>();
            if (c != null)
            {
                _spawned.Add(c);
            }
            else
            {
                _props.Add(go); // a rock or a tree, for the pickaxe and the axe
            }

            Plugin.Log.LogInfo(string.Format("spawned {0} at {1} ({2:0.0} m ahead)", prefabName, pos, ahead));
        }

        private void ReportCreatures(double yOffset)
        {
            int i = 0;
            foreach (Character c in _spawned)
            {
                if (c == null)
                {
                    Plugin.Log.LogInfo("creature " + i++ + ": gone");
                    continue;
                }

                Vector3 p = c.transform.position;
                Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "creature {0} {1}: hp {2:0.0}/{3:0.0} dead={4} pos={5:0.00},{6:0.00},{7:0.00}",
                    i++, c.m_name, c.GetHealth(), c.GetMaxHealth(), c.IsDead(), p.x, p.y, p.z));
            }
        }

        /// <summary>Primed TNT in Minecraft, `side` metres east of the first spawned creature.</summary>
        private void Tnt(double yOffset, float side)
        {
            Character c = _spawned.Count > 0 ? _spawned[0] : null;
            if (c == null)
            {
                Plugin.Log.LogWarning("tnt: spawn a creature first");
                return;
            }

            Vector3 p = c.transform.position;
            double mx, my, mz;
            Mapping.Position(p.x + side, p.y + 0.5, p.z, yOffset, out mx, out my, out mz);
            Link.Send(Messages.Command(string.Format(CultureInfo.InvariantCulture, "summon minecraft:tnt {0:0.###} {1:0.###} {2:0.###} {{fuse:4}}", mx, my, mz)));
        }

        // ------------------------------------------------------------------ ground check

        private void GroundCheck(Player player, double yOffset, int count)
        {
            if (player == null || !Link.Connected)
            {
                Plugin.Log.LogWarning("groundcheck: needs a player and the Minecraft link");
                return;
            }

            Vector3 at = player.transform.position;
            int cx = Mathf.FloorToInt(at.x), cz = Mathf.FloorToInt(-at.z);
            var cells = new List<int>();
            _expected = new List<int>();
            _columns = _freshMatches = 0;
            int mask = GroundProbe.Mask;
            var hits = new RaycastHit[16];
            foreach (KeyValuePair<long, int> kv in Plugin.Ground.Sent)
            {
                int x, z;
                GroundProbe.Unpack(kv.Key, out x, out z);
                if (Mathf.Abs(x - cx) > 14 || Mathf.Abs(z - cz) > 14)
                {
                    continue;
                }

                if (_columns >= count)
                {
                    break;
                }

                _columns++;
                int top = kv.Value;
                // the barrier's top and the cell under it are barriers, the cell above is not
                cells.AddRange(new[] { x, top, z, x, top - 1, z, x, top + 1, z });
                _expected.AddRange(new[] { 1, 1, 0 });
                // and the column still agrees with Valheim's ground right now
                int n = Physics.RaycastNonAlloc(new Vector3(x + 0.5f, at.y + 80f, -(z + 0.5f)), Vector3.down, hits, 260f, mask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue, surface = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (hits[j].collider.name != BlockColliders.ObjectName && hits[j].distance < best)
                    {
                        best = hits[j].distance;
                        surface = hits[j].point.y;
                    }
                }

                if (best < float.MaxValue && Mathf.FloorToInt((float)(surface + yOffset)) - 1 == top)
                {
                    _freshMatches++;
                }
            }

            if (_columns == 0)
            {
                Plugin.Log.LogWarning("groundcheck: no ground columns sent near the player yet");
                _expected = null;
                return;
            }

            Link.Send(Messages.Probe(cells));
        }

        // ------------------------------------------------------------------ items and buildings

        private static void DropItem(Player player, string prefabName, int count)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (player == null || prefab == null)
            {
                Plugin.Log.LogWarning("drop: needs a player and a Valheim item named " + prefabName);
                return;
            }

            Inventory inv = player.GetInventory();
            inv.AddItem(prefab, count);
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName && item.m_stack >= count)
                {
                    bool dropped = player.DropItem(inv, item, count);
                    Plugin.Log.LogInfo(string.Format("drop: {0} {1}: {2}", count, prefabName, dropped ? "dropped" : "refused"));
                    return;
                }
            }

            Plugin.Log.LogWarning("drop: " + prefabName + " did not arrive in the inventory");
        }

        private static void McMob(Player player, double yOffset, string type, float ahead)
        {
            Vector3 at;
            if (!GroundAhead(player, ahead, out at))
            {
                Plugin.Log.LogWarning("mcmob: no ground ahead");
                return;
            }

            double mx, my, mz;
            Mapping.Position(at.x, at.y + 0.1, at.z, yOffset, out mx, out my, out mz);
            Link.Send(Messages.Command(string.Format(CultureInfo.InvariantCulture, "summon minecraft:{0} {1:0.###} {2:0.###} {3:0.###} {{PersistenceRequired:1b}}", type, mx, my, mz)));
            Plugin.Log.LogInfo(string.Format("mcmob: {0} at {1}", type, at));
        }

        private static void McBuild(Player player, double yOffset, float ahead)
        {
            Vector3 at;
            if (!GroundAhead(player, ahead, out at))
            {
                Plugin.Log.LogWarning("mcbuild: no ground ahead");
                return;
            }

            double mx, my, mz;
            Mapping.Position(at.x, at.y, at.z, yOffset, out mx, out my, out mz);
            int x = (int)System.Math.Floor(mx), y = (int)System.Math.Floor(my), z = (int)System.Math.Floor(mz);
            string F(string a, string b, string block) => "fill " + a + " " + b + " " + block;
            string P(int dx, int dy, int dz) => (x + dx) + " " + (y + dy) + " " + (z + dz);
            foreach (string c in new[]
            {
                F(P(-3, 0, -3), P(3, 0, 3), "minecraft:cobblestone"),
                F(P(-3, 1, -3), P(3, 3, 3), "minecraft:oak_planks hollow"),
                F(P(-3, 1, -3), P(-3, 3, -3), "minecraft:oak_log"), F(P(3, 1, -3), P(3, 3, -3), "minecraft:oak_log"),
                F(P(-3, 1, 3), P(-3, 3, 3), "minecraft:oak_log"), F(P(3, 1, 3), P(3, 3, 3), "minecraft:oak_log"),
                F(P(-2, 4, -3), P(2, 4, 3), "minecraft:stone_bricks"), F(P(-1, 5, -3), P(1, 5, 3), "minecraft:stone_bricks"),
                F(P(0, 6, -3), P(0, 6, 3), "minecraft:stone_bricks"),
                F(P(-3, 2, -1), P(-3, 2, 1), "minecraft:glass_pane"), F(P(3, 2, -1), P(3, 2, 1), "minecraft:glass_pane"),
                F(P(-1, 2, -3), P(1, 2, -3), "minecraft:glass_pane"), F(P(-1, 2, 3), P(1, 2, 3), "minecraft:glass_pane"),
                "setblock " + P(-4, 3, 0) + " minecraft:wall_torch[facing=west]", "setblock " + P(4, 3, 0) + " minecraft:wall_torch[facing=east]",
                "setblock " + P(0, 3, -4) + " minecraft:wall_torch[facing=north]", "setblock " + P(0, 3, 4) + " minecraft:wall_torch[facing=south]",
                F(P(-5, 1, -5), P(-5, 1, -5), "minecraft:poppy"), F(P(5, 1, 5), P(5, 1, 5), "minecraft:dandelion"),
            })
            {
                Link.Send(Messages.Command(c));
            }

            Plugin.Log.LogInfo(string.Format("mcbuild: a cottage at Minecraft {0} {1} {2}", x, y, z));
        }

        private static void HitBody(Player player, float damage)
        {
            Character best = null;
            float bestD = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                float d = c != null && player != null && MobBridge.IsBody(c) ? Vector3.Distance(c.transform.position, player.transform.position) : float.MaxValue;
                if (d < bestD)
                {
                    best = c;
                    bestD = d;
                }
            }

            if (best == null)
            {
                Plugin.Log.LogWarning("hitbody: no Minecraft mob stands in Valheim");
                return;
            }

            var hit = new HitData { m_point = best.GetCenterPoint(), m_dir = (best.transform.position - player.transform.position).normalized, m_pushForce = 10f };
            hit.m_damage.m_slash = damage;
            hit.m_attacker = player.GetZDOID();
            best.Damage(hit);
            Plugin.Log.LogInfo(string.Format("hitbody: {0} at {1:0.0} m for {2}", best.m_name, bestD, damage));
        }

        private static int CountItem(Player player, string prefabName)
        {
            int n = 0;
            if (player != null)
            {
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    n += item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName ? item.m_stack : 0;
                }
            }

            return n;
        }

        // a voxelcheck in flight: how many cells of the spawned building piece were probed
        private int _voxelCells = -1;

        /// <summary>Ask Minecraft whether the cells the newest spawned building piece was voxelized into hold barriers.</summary>
        private void VoxelCheck(double yOffset)
        {
            WearNTear piece = null;
            for (int i = _props.Count - 1; i >= 0 && piece == null; i--)
            {
                piece = _props[i] != null ? _props[i].GetComponent<WearNTear>() : null;
            }

            List<long> cells = piece != null ? Plugin.Pieces.CellsOf(piece) : null;
            if (cells == null || cells.Count == 0)
            {
                Plugin.Log.LogWarning("voxelcheck: no voxelized building piece among the spawned props");
                return;
            }

            var probe = new List<int>();
            foreach (long key in cells)
            {
                int x, y, z;
                BarrierCells.Unpack(key, out x, out y, out z);
                probe.AddRange(new[] { x, y, z });
            }

            _voxelCells = cells.Count;
            Plugin.Log.LogInfo("voxelcheck: " + piece.name + " is " + cells.Count + " cells, probing Minecraft");
            Link.Send(Messages.Probe(probe));
        }

        public void OnProbeReply(string json)
        {
            if (_voxelCells >= 0)
            {
                List<int> v = Messages.IntArray(json, "r");
                int barriers = 0;
                foreach (int b in v)
                {
                    barriers += b;
                }

                Plugin.Log.LogInfo(string.Format("voxelcheck: {0}/{1} cells of the piece are barriers in Minecraft", barriers, _voxelCells));
                _voxelCells = -1;
                return;
            }

            if (_expected == null)
            {
                return;
            }

            List<int> r = Messages.IntArray(json, "r");
            if (r.Count != _expected.Count)
            {
                Plugin.Log.LogWarning("groundcheck: reply has " + r.Count + " values, expected " + _expected.Count);
                _expected = null;
                return;
            }

            int topOk = 0, belowOk = 0, aboveOk = 0;
            for (int i = 0; i < r.Count; i += 3)
            {
                topOk += r[i] == _expected[i] ? 1 : 0;
                belowOk += r[i + 1] == _expected[i + 1] ? 1 : 0;
                aboveOk += r[i + 2] == _expected[i + 2] ? 1 : 0;
            }

            Plugin.Log.LogInfo(string.Format(
                "groundcheck: {0} columns: barrier at the top cell {1}/{0}, in the cell below {2}/{0}, cell above empty {3}/{0}, still equal to a fresh Valheim raycast {4}/{0}",
                _columns, topOk, belowOk, aboveOk, _freshMatches));
            _expected = null;
        }
    }
}
