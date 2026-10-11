using System;
using System.Collections.Generic;
using System.Globalization;

namespace MineHeim.Core
{
    /// <summary>
    /// A held item as Minecraft sent it ({"t":"held",...}: quads in the arm's own space, Minecraft's model axes, blocks) turned
    /// into a mesh for Steve's arm (SteveModel's axes and pixels, Unity's winding). Quads with see-through pixels (a sword's
    /// sprite is a square with the sword in it) become one small square per opaque pixel, so no alpha test is needed, as
    /// for the skin's outer layer.
    /// </summary>
    public static class HeldItemMesh
    {
        /// <summary>
        /// Minecraft's arm space (blocks, y down, facing -z) to SteveModel's arm space (pixels, y up, facing +z): the model
        /// mapping (x, y, z) -> (-x, -y, -z), times 16. A slim arm's item sits half a pixel further in, as Minecraft's
        /// translateToHand moves a slim arm before the item is placed.
        /// </summary>
        public static void ToSteve(float x, float y, float z, bool slim, bool rightArm, out float sx, out float sy, out float sz)
        {
            sx = -16f * x + (slim ? (rightArm ? -0.5f : 0.5f) : 0f);
            sy = -16f * y;
            sz = -16f * z;
        }

        /// <summary>
        /// The mesh: `xyz` 12 numbers per quad (4 corners), `uv` 8 per quad (0..1, v down) into a texW x texH image whose
        /// opaque(x, y) pixels (from the top left) are drawn. Uvs come out v up, as Unity reads them.
        /// </summary>
        public static SteveModel.Mesh Build(float[] xyz, float[] uv, int texW, int texH, Func<int, int, bool> opaque, bool slim, bool rightArm)
        {
            var mesh = new SteveModel.Mesh();
            int quads = Math.Min(xyz.Length / 12, uv.Length / 8);
            var p = new float[12];
            var t = new float[8];
            for (int q = 0; q < quads; q++)
            {
                for (int i = 0; i < 4; i++)
                {
                    ToSteve(xyz[q * 12 + i * 3], xyz[q * 12 + i * 3 + 1], xyz[q * 12 + i * 3 + 2], slim, rightArm, out p[i * 3], out p[i * 3 + 1], out p[i * 3 + 2]);
                    t[i * 2] = uv[q * 8 + i * 2] * texW;
                    t[i * 2 + 1] = uv[q * 8 + i * 2 + 1] * texH;
                }

                AddQuad(mesh, p, t, texW, texH, opaque);
            }

            return mesh;
        }

        /// <summary>One quad (corners 0..3, texel coordinates `t`): whole if all its pixels are opaque, else per opaque pixel.</summary>
        private static void AddQuad(SteveModel.Mesh mesh, float[] p, float[] t, int texW, int texH, Func<int, int, bool> opaque)
        {
            float u0 = Math.Min(Math.Min(t[0], t[2]), Math.Min(t[4], t[6])), u1 = Math.Max(Math.Max(t[0], t[2]), Math.Max(t[4], t[6]));
            float v0 = Math.Min(Math.Min(t[1], t[3]), Math.Min(t[5], t[7])), v1 = Math.Max(Math.Max(t[1], t[3]), Math.Max(t[5], t[7]));
            int x0 = Clamp((int)Math.Floor(u0 + 1e-3f), texW), x1 = Clamp((int)Math.Ceiling(u1 - 1e-3f) - 1, texW);
            int y0 = Clamp((int)Math.Floor(v0 + 1e-3f), texH), y1 = Clamp((int)Math.Ceiling(v1 - 1e-3f) - 1, texH);
            x1 = Math.Max(x0, x1);
            y1 = Math.Max(y0, y1);
            bool all = true, any = false;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    bool o = opaque == null || opaque(x, y);
                    all &= o;
                    any |= o;
                }
            }

            if (!any)
            {
                return;
            }

            // a thin quad (an extruded sprite's side: a pixel wide, or none) or a whole opaque one: as it is
            bool thin = u1 - u0 < 1.001f || v1 - v0 < 1.001f;
            if (all || thin)
            {
                Emit(mesh, p, t, texW, texH, new[] { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f });
                return;
            }

            // texel (x, y)'s corners back to the quad's (s, t): uv = A + s (B - A) + t (D - A), a parallelogram (rectangles are)
            float ax = t[0], ay = t[1], bx = t[2] - ax, by = t[3] - ay, dx = t[6] - ax, dy = t[7] - ay;
            float det = bx * dy - by * dx;
            if (Math.Abs(det) < 1e-6f)
            {
                return;
            }

            var st = new float[8];
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (opaque != null && !opaque(x, y))
                    {
                        continue;
                    }

                    // the texel's corners in the order the quad's own corners go round (so the winding is kept)
                    float[] cornersU = { CornerU(t, 0, x), CornerU(t, 1, x), CornerU(t, 2, x), CornerU(t, 3, x) };
                    float[] cornersV = { CornerV(t, 0, y), CornerV(t, 1, y), CornerV(t, 2, y), CornerV(t, 3, y) };
                    for (int c = 0; c < 4; c++)
                    {
                        float ru = cornersU[c] - ax, rv = cornersV[c] - ay;
                        st[c * 2] = (ru * dy - rv * dx) / det;
                        st[c * 2 + 1] = (bx * rv - by * ru) / det;
                    }

                    Emit(mesh, p, t, texW, texH, st);
                }
            }
        }

        /// <summary>The u of texel x's edge on the side where quad corner c's u lies (corners keep their place round the quad).</summary>
        private static float CornerU(float[] t, int c, int x)
        {
            float mid = (t[0] + t[2] + t[4] + t[6]) / 4f;
            return t[c * 2] <= mid ? x : x + 1;
        }

        private static float CornerV(float[] t, int c, int y)
        {
            float mid = (t[1] + t[3] + t[5] + t[7]) / 4f;
            return t[c * 2 + 1] <= mid ? y : y + 1;
        }

        /// <summary>The part of the quad at (s, t) per corner (4 pairs), with Unity's winding (the reversed axes mirror it).</summary>
        private static void Emit(SteveModel.Mesh mesh, float[] p, float[] t, int texW, int texH, float[] st)
        {
            int first = mesh.VertexCount;
            for (int c = 0; c < 4; c++)
            {
                float s = st[c * 2], r = st[c * 2 + 1];
                for (int k = 0; k < 3; k++)
                {
                    mesh.Positions.Add(p[k] + s * (p[3 + k] - p[k]) + r * (p[9 + k] - p[k]));
                }

                float u = t[0] + s * (t[2] - t[0]) + r * (t[6] - t[0]);
                float v = t[1] + s * (t[3] - t[1]) + r * (t[7] - t[1]);
                mesh.Uvs.Add(u / texW);
                mesh.Uvs.Add(1f - v / texH);
            }

            mesh.Triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }

        private static int Clamp(int v, int size) => Math.Max(0, Math.Min(size - 1, v));

        /// <summary>The numbers of `"key":[...]` in a message (empty if missing): the held item's corners and uvs.</summary>
        public static float[] Numbers(string json, string key)
        {
            int at = json.IndexOf("\"" + key + "\":[", StringComparison.Ordinal);
            if (at < 0)
            {
                return new float[0];
            }

            int start = at + key.Length + 4, end = json.IndexOf(']', start);
            if (end < 0)
            {
                return new float[0];
            }

            var list = new List<float>((end - start) / 7 + 1);
            int i = start;
            while (i < end)
            {
                int comma = json.IndexOf(',', i);
                if (comma < 0 || comma > end)
                {
                    comma = end;
                }

                float f;
                if (comma > i && float.TryParse(json.Substring(i, comma - i), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                {
                    list.Add(f);
                }

                i = comma + 1;
            }

            return list.ToArray();
        }
    }
}
