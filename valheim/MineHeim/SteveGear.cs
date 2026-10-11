using System;
using System.Collections.Generic;
using MineHeim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MineHeim
{
    /// <summary>
    /// What Steve holds and wears when Valheim draws him (Steve.Avatar = Native): Minecraft sends each hand's item as its own
    /// model ({"t":"held"}: the very quads Minecraft draws, display transform and all, with their texture) and each armour
    /// piece's texture ({"t":"armor"}), on change. The item goes on the arm that holds it, the armour on Steve's own boxes
    /// grown as Minecraft grows armour. Nothing is modelled by hand.
    /// </summary>
    internal static class SteveGear
    {
        private sealed class Held
        {
            public string Item = "";
            public bool RightArm;
            public float[] Xyz, Uv;
            public Texture2D Texture;
            public GameObject Shown;
        }

        private sealed class Worn
        {
            public string Item = "";
            public Texture2D Texture;
            public readonly List<GameObject> Shown = new List<GameObject>();
        }

        private static readonly Held[] Hands = { new Held(), new Held() };
        private static readonly Worn[] Armour = { new Worn(), new Worn(), new Worn(), new Worn() };
        private static readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();

        /// <summary>For the status line: what he holds and wears.</summary>
        internal static string Summary => (Hands[0].Item.Length > 0 ? Hands[0].Item : "empty hand") + (Hands[1].Item.Length > 0 ? " + " + Hands[1].Item : "") +
                                          ((Armour[0].Item + Armour[1].Item + Armour[2].Item + Armour[3].Item).Length > 0 ? ", armour" : "");

        /// <summary>Minecraft (re)connected: ask for everything again.</summary>
        internal static void Request() => Link.Send("{\"t\":\"heldreq\"}");

        /// <summary>{"t":"held","hand":"main|off","arm":"right|left","item":id,"v":[...],"uv":[...],"png":base64}</summary>
        internal static void OnHeld(string message)
        {
            Held h = Hands[Messages.StringValue(message, "hand", "main") == "off" ? 1 : 0];
            Clear(h);
            h.Item = Messages.StringValue(message, "item", "");
            h.RightArm = Messages.StringValue(message, "arm", "right") != "left";
            if (h.Item.Length > 0)
            {
                h.Xyz = HeldItemMesh.Numbers(message, "v");
                h.Uv = HeldItemMesh.Numbers(message, "uv");
                h.Texture = Png(Messages.StringValue(message, "png", ""));
                if (h.Texture == null || h.Xyz.Length < 12)
                {
                    h.Item = "";
                }
            }

            AttachHeld(h);
        }

        /// <summary>{"t":"armor","slot":"head|chest|legs|feet","item":id,"png":base64}</summary>
        internal static void OnArmour(string message)
        {
            int slot = Array.IndexOf(new[] { "head", "chest", "legs", "feet" }, Messages.StringValue(message, "slot", ""));
            if (slot < 0)
            {
                return;
            }

            Worn w = Armour[slot];
            Clear(w);
            w.Item = Messages.StringValue(message, "item", "");
            if (w.Item.Length > 0)
            {
                w.Texture = Png(Messages.StringValue(message, "png", ""));
                if (w.Texture == null)
                {
                    w.Item = "";
                }
            }

            AttachArmour(slot);
        }

        /// <summary>Steve's model was (re)built, or his arms changed (slim or wide): put everything on him again.</summary>
        internal static void Attach()
        {
            foreach (Held h in Hands)
            {
                AttachHeld(h);
            }

            for (int s = 0; s < Armour.Length; s++)
            {
                AttachArmour(s);
            }
        }

        internal static void SetShadow(ShadowCastingMode mode)
        {
            foreach (MeshRenderer r in Renderers)
            {
                if (r != null)
                {
                    r.shadowCastingMode = mode;
                }
            }
        }

        // ------------------------------------------------------------------ building

        private static void AttachHeld(Held h)
        {
            Destroy(h.Shown);
            h.Shown = null;
            Transform arm = SteveAvatar.PartTransform(h.RightArm ? SteveModel.Part.RightArm : SteveModel.Part.LeftArm);
            if (arm == null || h.Item.Length == 0)
            {
                return;
            }

            SteveModel.Mesh m = HeldItemMesh.Build(h.Xyz, h.Uv, h.Texture.width, h.Texture.height, Opaque(h.Texture), SteveAvatar.Slim, h.RightArm);
            h.Shown = Show("Held " + h.Item, arm, m, h.Texture);
        }

        private static void AttachArmour(int slot)
        {
            Worn w = Armour[slot];
            foreach (GameObject go in w.Shown)
            {
                Destroy(go);
            }

            w.Shown.Clear();
            if (w.Item.Length == 0 || SteveAvatar.PartTransform(SteveModel.Part.Head) == null)
            {
                return;
            }

            Func<int, int, bool> opaque = Opaque(w.Texture);
            for (int p = 0; p < SteveModel.PartCount; p++)
            {
                SteveModel.Mesh m = SteveModel.BuildArmour((SteveModel.Part)p, (SteveModel.ArmourSlot)slot, w.Texture.width, w.Texture.height, opaque);
                if (m.VertexCount > 0)
                {
                    w.Shown.Add(Show("Armour " + w.Item, SteveAvatar.PartTransform((SteveModel.Part)p), m, w.Texture));
                }
            }
        }

        private static GameObject Show(string name, Transform parent, SteveModel.Mesh m, Texture2D texture)
        {
            var vertices = new List<Vector3>(m.VertexCount);
            var uvs = new List<Vector2>(m.VertexCount);
            for (int i = 0; i < m.VertexCount; i++)
            {
                vertices.Add(new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]));
                uvs.Add(new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]));
            }

            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(m.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = SteveAvatar.GearMaterial(texture);
            r.shadowCastingMode = SteveAvatar.ShadowMode;
            r.receiveShadows = true;
            Renderers.RemoveAll(x => x == null);
            Renderers.Add(r);
            return go;
        }

        /// <summary>A texture's drawn pixels, (x, y) from the top left as Minecraft numbers them (Unity's rows run bottom-up).</summary>
        private static Func<int, int, bool> Opaque(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int w = texture.width, h = texture.height;
            return (x, y) => x >= 0 && y >= 0 && x < w && y < h && pixels[(h - 1 - y) * w + x].a >= 128;
        }

        private static Texture2D Png(string base64)
        {
            if (base64.Length == 0)
            {
                return null;
            }

            try
            {
                return SteveAvatar.LoadPng(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static void Clear(Held h)
        {
            Destroy(h.Shown);
            h.Shown = null;
            if (h.Texture != null)
            {
                UnityEngine.Object.Destroy(h.Texture);
                h.Texture = null;
            }

            h.Xyz = h.Uv = null;
        }

        private static void Clear(Worn w)
        {
            foreach (GameObject go in w.Shown)
            {
                Destroy(go);
            }

            w.Shown.Clear();
            if (w.Texture != null)
            {
                UnityEngine.Object.Destroy(w.Texture);
                w.Texture = null;
            }
        }

        private static void Destroy(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            MeshFilter f = go.GetComponent<MeshFilter>();
            MeshRenderer r = go.GetComponent<MeshRenderer>();
            if (f != null && f.sharedMesh != null)
            {
                UnityEngine.Object.Destroy(f.sharedMesh);
            }

            if (r != null && r.sharedMaterial != null)
            {
                UnityEngine.Object.Destroy(r.sharedMaterial);
            }

            UnityEngine.Object.Destroy(go);
        }
    }
}
