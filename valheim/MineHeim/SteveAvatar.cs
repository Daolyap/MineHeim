using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Steve, drawn by Valheim itself (Steve.Avatar = Native). Minecraft's picture of him arrives a frame or two after
    /// Valheim's, so anything that moves with the camera (Steve above all) trailed behind and doubled up whenever the
    /// Viking ran or turned. Here he is Minecraft's player model (SteveModel) put on the Viking's own position and facing
    /// just before the camera culls: the same frame, no delay, depth-tested by Valheim like any of its own objects.
    /// Minecraft stops drawing his body ({"t":"steve","body":false}) and sends his skin ({"t":"skin"}) and arm swings
    /// ({"t":"armswing"}); the walk, crouch, sitting and head turns are Valheim's own.
    /// </summary>
    internal static class SteveAvatar
    {
        // standing at a ship's helm is "attached" too: only a seat (not on a ship) sits him down
        private static readonly FieldInfo AttachedToShip = AccessTools.Field(typeof(Player), "m_attachedToShip");
        private static readonly string[] ShaderNames = { "Standard", "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Unlit/Texture" };

        private static GameObject _root;
        private static readonly Transform[] Parts = new Transform[SteveModel.PartCount];
        private static readonly MeshFilter[] Filters = new MeshFilter[SteveModel.PartCount];
        private static readonly MeshRenderer[] Renderers = new MeshRenderer[SteveModel.PartCount];
        private static readonly Vector3[] Pivots = new Vector3[SteveModel.PartCount];
        private static Material _material;
        private static Texture2D _skin;
        private static Color32[] _pixels;
        private static bool _slim, _meshDirty = true;

        private static readonly SteveModel.WalkCycle Walk = new SteveModel.WalkCycle();
        private static readonly SteveModel.Swing Arm = new SteveModel.Swing();
        private static readonly float[] Rot = new float[SteveModel.PartCount * 3], Off = new float[SteveModel.PartCount * 3];
        private static float _age, _bodyYaw;
        private static bool _haveLast;
        private static Vector3 _lastPos;
        private static int _lastFrame = -1;
        private static Camera _camera;
        private static ShadowCastingMode _shadowMode = ShadowCastingMode.On;

        /// <summary>Where Steve's skin came from (for the perf line and the tests).</summary>
        internal static string SkinSource = "placeholder";

        internal static bool Native => Plugin.SteveAvatarMode.Value == Plugin.AvatarMode.Native;

        /// <summary>Shown right now (status line).</summary>
        internal static bool Shown => _root != null && _root.activeSelf;

        internal static void Enable() => Camera.onPreCull += HandlePreCull;

        internal static void Disable()
        {
            Camera.onPreCull -= HandlePreCull;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // ------------------------------------------------------------------ the skin

        /// <summary>A Minecraft world started or the skin setting changed: ask for the skin Steve.Skin names.</summary>
        internal static void RequestSkin()
        {
            Plugin.SkinChoice choice = Plugin.SteveSkin.Value;
            if (choice == Plugin.SkinChoice.File)
            {
                Link.Send("{\"t\":\"skinreq\",\"mode\":\"steve\"}"); // Minecraft's own model (Steve.Avatar = Minecraft) stays Steve
                LoadSkinFile(Plugin.SteveSkinFile.Value, Plugin.SteveSkinSlim.Value);
                return;
            }

            Link.Send("{\"t\":\"skinreq\",\"mode\":\"" + (choice == Plugin.SkinChoice.Account ? "account" : "steve") + "\"}");
        }

        /// <summary>{"t":"skin","png":base64,"slim":bool,"src":"steve|account","err":why}</summary>
        internal static void OnSkin(string message)
        {
            string error = Messages.StringValue(message, "err", "");
            string source = Messages.StringValue(message, "src", "minecraft");
            if (error.Length > 0)
            {
                Plugin.Log.LogWarning("steve skin (" + source + "): " + error);
            }

            if (Plugin.SteveSkin.Value == Plugin.SkinChoice.File)
            {
                return; // the file wins; Minecraft only answered for its own model
            }

            string png = Messages.StringValue(message, "png", "");
            if (png.Length == 0)
            {
                return;
            }

            try
            {
                SetSkin(Convert.FromBase64String(png), Messages.BoolValue(message, "slim", false), source);
            }
            catch (FormatException e)
            {
                Plugin.Log.LogWarning("steve skin: bad data from Minecraft: " + e.Message);
            }
        }

        private static void LoadSkinFile(string path, bool slim)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path.Length == 0)
            {
                Plugin.Log.LogWarning("steve skin: Steve.Skin is File but Steve.SkinFile is empty");
                return;
            }

            try
            {
                SetSkin(File.ReadAllBytes(path), slim, "file " + Path.GetFileName(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                Plugin.Log.LogWarning("steve skin: cannot read " + path + ": " + e.Message);
            }
        }

        private static void SetSkin(byte[] png, bool slim, string source)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, png, false) || texture.width != 64 || (texture.height != 64 && texture.height != 32))
            {
                Plugin.Log.LogWarning("steve skin (" + source + "): not a 64x64 or 64x32 PNG (" + texture.width + "x" + texture.height + "), keeping the old one");
                UnityEngine.Object.Destroy(texture);
                return;
            }

            UseTexture(texture, slim);
            SkinSource = source;
            Plugin.Log.LogInfo("steve skin: " + source + (slim ? " (slim arms)" : "") + ", " + texture.width + "x" + texture.height);
        }

        private static void UseTexture(Texture2D texture, bool slim)
        {
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (_skin != null && _skin != texture)
            {
                UnityEngine.Object.Destroy(_skin);
            }

            _skin = texture;
            _pixels = texture.GetPixels32();
            _slim = slim;
            _meshDirty = true;
            if (_material != null)
            {
                _material.mainTexture = texture;
            }
        }

        private static void PlaceholderSkin()
        {
            byte[] rgba = SteveModel.PlaceholderSkin();
            var colors = new Color32[64 * 64];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            }

            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.SetPixels32(colors);
            texture.Apply(false);
            UseTexture(texture, false);
        }

        /// <summary>{"t":"armswing"}: Minecraft swung Steve's arm (attack, mining, placing, using).</summary>
        internal static void OnArmSwing() => Arm.Start();

        // ------------------------------------------------------------------ the model

        private static void Build(Player player)
        {
            _root = new GameObject("MineHeim Steve");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            GameObject visual = player.GetVisual();
            int layer = visual != null ? visual.layer : player.gameObject.layer;
            _root.layer = layer;
            _material = MakeMaterial(player);
            if (_skin == null)
            {
                PlaceholderSkin();
            }

            _material.mainTexture = _skin;
            for (int p = 0; p < SteveModel.PartCount; p++)
            {
                var go = new GameObject(((SteveModel.Part)p).ToString()) { layer = layer };
                go.transform.SetParent(_root.transform, false);
                Parts[p] = go.transform;
                Filters[p] = go.AddComponent<MeshFilter>();
                Renderers[p] = go.AddComponent<MeshRenderer>();
                Renderers[p].sharedMaterial = _material;
                Renderers[p].shadowCastingMode = ShadowCastingMode.On;
                Renderers[p].receiveShadows = true;
            }

            _meshDirty = true;
            _shadowMode = ShadowCastingMode.On;
        }

        /// <summary>
        /// An opaque lit material: the outer layer is built from the skin's opaque pixels only, so no alpha test is needed.
        /// The first of a few standard shaders the game has, else whatever draws the Viking.
        /// </summary>
        private static Material MakeMaterial(Player player)
        {
            foreach (string name in ShaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader == null || !shader.isSupported)
                {
                    continue;
                }

                var m = new Material(shader) { name = "MineHeim Steve" };
                if (m.HasProperty("_Glossiness"))
                {
                    m.SetFloat("_Glossiness", 0f); // cloth and skin, not plastic
                }

                if (m.HasProperty("_Metallic"))
                {
                    m.SetFloat("_Metallic", 0f);
                }

                Plugin.Log.LogInfo("steve: drawn by Valheim with the " + name + " shader");
                return m;
            }

            Renderer viking = player.GetVisual() != null ? player.GetVisual().GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            Material fallback = viking != null && viking.sharedMaterial != null
                ? new Material(viking.sharedMaterial) { name = "MineHeim Steve" }
                : new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored")) { name = "MineHeim Steve" };
            Plugin.Log.LogWarning("steve: no standard shader in this build, using " + fallback.shader.name);
            return fallback;
        }

        private static void RebuildMeshes()
        {
            _meshDirty = false;
            int w = _skin.width, h = _skin.height;
            Color32[] pixels = _pixels;
            Func<int, int, bool> opaque = (x, y) => pixels[(h - 1 - y) * w + x].a >= 128;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int p = 0; p < SteveModel.PartCount; p++)
            {
                var part = (SteveModel.Part)p;
                SteveModel.Mesh m = SteveModel.Build(part, _slim, w, h, opaque);
                vertices.Clear();
                uvs.Clear();
                for (int i = 0; i < m.VertexCount; i++)
                {
                    vertices.Add(new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]));
                    uvs.Add(new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]));
                }

                Mesh mesh = Filters[p].sharedMesh;
                if (mesh == null)
                {
                    mesh = new Mesh { name = "Steve " + part };
                    mesh.MarkDynamic();
                    Filters[p].sharedMesh = mesh;
                }

                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(m.Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                float px, py, pz;
                SteveModel.Pivot(part, _slim, out px, out py, out pz);
                Pivots[p] = new Vector3(px, py, pz);
            }
        }

        // ------------------------------------------------------------------ every frame

        /// <summary>Just before the main camera culls: the same frame Valheim draws, so Steve is exactly on the Viking.</summary>
        private static void HandlePreCull(Camera cam)
        {
            if (GameCamera.instance == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = GameCamera.instance.GetComponent<Camera>();
            }

            if (cam != _camera || Time.frameCount == _lastFrame)
            {
                return;
            }

            _lastFrame = Time.frameCount;
            Player player = Player.m_localPlayer;
            bool show = player != null && Native && Plugin.Enabled.Value && Link.Connected && InputBridge.McMode && !player.IsDead();
            if (!show)
            {
                if (_root != null && _root.activeSelf)
                {
                    _root.SetActive(false);
                }

                _haveLast = false;
                return;
            }

            if (_root == null)
            {
                Build(player);
            }

            if (!_root.activeSelf)
            {
                _root.SetActive(true);
            }

            if (_meshDirty)
            {
                RebuildMeshes();
            }

            Pose(player, cam);
        }

        private static void Pose(Player player, Camera cam)
        {
            float dt = Time.deltaTime;
            Vector3 pos = player.transform.position;
            float speed = 0f;
            if (_haveLast && dt > 1e-4f)
            {
                Vector3 moved = pos - _lastPos;
                moved.y = 0f;
                speed = moved.magnitude / dt;
                if (speed > 40f)
                {
                    speed = 0f; // a teleport is not a stride
                }
            }

            if (!_haveLast)
            {
                _bodyYaw = player.transform.eulerAngles.y;
            }

            _lastPos = pos;
            _haveLast = true;
            Walk.Step(speed, dt);
            Arm.Step(dt);
            _age += dt;

            // the body turns where the Viking turns while he moves; standing, it follows the head once that has turned
            // more than 50 degrees (as Minecraft's does). The head looks where the Viking looks.
            Vector3 look = player.GetLookDir();
            float lookYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            if (speed > 0.5f)
            {
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, player.transform.eulerAngles.y, 540f * dt);
            }

            float headYaw = Mathf.DeltaAngle(_bodyYaw, lookYaw);
            if (Mathf.Abs(headYaw) > 50f)
            {
                _bodyYaw += headYaw - Mathf.Sign(headYaw) * 50f;
                headYaw = Mathf.Sign(headYaw) * 50f;
            }

            bool swimming = player.IsSwimming();
            bool sitting = player.IsAttached() && !(AttachedToShip != null && (bool)AttachedToShip.GetValue(player));
            var input = new SteveModel.Input
            {
                WalkPosition = Walk.Position,
                WalkAmount = sitting ? 0f : Walk.Amount,
                Attack = Arm.Progress,
                HeadPitch = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)),
                HeadYaw = headYaw * Mathf.Deg2Rad,
                Age = _age,
                Crouch = player.IsCrouching() && !swimming,
                Sit = sitting,
                Swim = swimming,
            };
            SteveModel.Animate(input, Rot, Off);

            // a swimming Viking keeps his head above the water and his feet far below it: Steve's eyes go where his are
            Vector3 feet = pos;
            if (swimming && player.m_eye != null)
            {
                feet.y = player.m_eye.position.y - 1.62f;
            }

            Transform root = _root.transform;
            root.SetPositionAndRotation(feet, Quaternion.Euler(0f, _bodyYaw, 0f));
            root.localScale = Vector3.one * SteveModel.Scale;
            for (int p = 0; p < SteveModel.PartCount; p++)
            {
                int i = p * 3;
                Parts[p].localPosition = Pivots[p] + new Vector3(Off[i], Off[i + 1], Off[i + 2]);
                // Minecraft's order: x first, then y, then z
                Parts[p].localRotation = Quaternion.AngleAxis(Rot[i + 2] * Mathf.Rad2Deg, Vector3.forward)
                                         * Quaternion.AngleAxis(Rot[i + 1] * Mathf.Rad2Deg, Vector3.up)
                                         * Quaternion.AngleAxis(Rot[i] * Mathf.Rad2Deg, Vector3.right);
            }

            // first person, or the camera pushed into his head against a wall: only his shadow
            Vector3 head = Parts[(int)SteveModel.Part.Head].position + root.up * 0.25f;
            float distance = PoseSender.CameraDistance();
            bool inside = (distance >= 0f && distance <= 0.01f) || (cam.transform.position - head).sqrMagnitude < 0.45f * 0.45f;
            ShadowCastingMode mode = inside ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (mode != _shadowMode)
            {
                _shadowMode = mode;
                foreach (MeshRenderer r in Renderers)
                {
                    r.shadowCastingMode = mode;
                }
            }
        }
    }
}
