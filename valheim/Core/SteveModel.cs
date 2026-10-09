using System;
using System.Collections.Generic;

namespace MineHeim.Core
{
    /// <summary>
    /// Minecraft's player model (the classic 64x64 skin layout, wide or slim arms) as plain boxes, and its animation
    /// (walk, idle, attack swing, crouch, sitting, a raised shield), Unity-free so it can be tested. The host draws Steve itself from this,
    /// on its own player in the same frame, instead of using Minecraft's picture of him (which arrives a frame or two
    /// late and trails behind whenever the player moves or turns).
    ///
    /// Units are model pixels (a block is 16), y up, the model faces +z with its right side at +x (Unity's axes; the
    /// host scales by Scale). Angles follow Minecraft's ModelPart: x positive tilts a part's top forward (a hanging arm
    /// swings back), y positive turns it right, z positive tilts its top to its left (a right arm hangs out to the side).
    /// </summary>
    public static class SteveModel
    {
        /// <summary>Metres per model pixel: a block is 16 pixels, and Minecraft draws players at 15/16 of their size.</summary>
        public const float Scale = 0.9375f / 16f;

        public enum Part
        {
            Head,
            Body,
            RightArm,
            LeftArm,
            RightLeg,
            LeftLeg,
        }

        public const int PartCount = 6;

        /// <summary>One part's geometry: xyz per vertex, uv per vertex (0..1, v up as Unity reads it), triangles.</summary>
        public sealed class Mesh
        {
            public readonly List<float> Positions = new List<float>();
            public readonly List<float> Uvs = new List<float>();
            public readonly List<int> Triangles = new List<int>();

            public int VertexCount => Positions.Count / 3;
        }

        /// <summary>Where a part turns, relative to the feet (pixels).</summary>
        public static void Pivot(Part part, bool slim, out float x, out float y, out float z)
        {
            z = 0f;
            switch (part)
            {
                case Part.Head:
                case Part.Body:
                    x = 0f;
                    y = 24f;
                    return;
                case Part.RightArm:
                    x = 5f;
                    y = slim ? 21.5f : 22f;
                    return;
                case Part.LeftArm:
                    x = -5f;
                    y = slim ? 21.5f : 22f;
                    return;
                case Part.RightLeg:
                    x = 1.9f;
                    y = 12f;
                    return;
                default:
                    x = -1.9f;
                    y = 12f;
                    return;
            }
        }

        /// <summary>
        /// A part's boxes around its pivot, textured from a skin of texWidth x texHeight pixels: the base layer, and the
        /// outer layer (hat, jacket, sleeves, trousers) slightly larger. An old 64x32 skin has only the hat, and its left
        /// limbs use the right limbs' pixels.
        ///
        /// `opaque(x, y)` (skin pixels, y down from the top) says which of the outer layer's pixels are there: the outer
        /// layer is built from those pixels only, one small square each, so it needs no alpha test (a shader variant the
        /// game may not ship). Null: whole faces.
        /// </summary>
        public static Mesh Build(Part part, bool slim, int texWidth, int texHeight, Func<int, int, bool> opaque = null)
        {
            var mesh = new Mesh();
            _opaque = opaque;
            bool modern = texHeight >= 64;
            int arm = slim ? 3 : 4;
            switch (part)
            {
                case Part.Head:
                    Box(mesh, -4, 0, -4, 8, 8, 8, 0, 0, 0f, texWidth, texHeight);
                    if (modern || opaque == null || !SolidHat(opaque))
                    {
                        Box(mesh, -4, 0, -4, 8, 8, 8, 32, 0, 0.5f, texWidth, texHeight);
                    }

                    break;
                case Part.Body:
                    Box(mesh, -4, -12, -2, 8, 12, 4, 16, 16, 0f, texWidth, texHeight);
                    if (modern)
                    {
                        Box(mesh, -4, -12, -2, 8, 12, 4, 16, 32, 0.25f, texWidth, texHeight);
                    }

                    break;
                case Part.RightArm:
                    Box(mesh, -1, -10, -2, arm, 12, 4, 40, 16, 0f, texWidth, texHeight);
                    if (modern)
                    {
                        Box(mesh, -1, -10, -2, arm, 12, 4, 40, 32, 0.25f, texWidth, texHeight);
                    }

                    break;
                case Part.LeftArm:
                    Box(mesh, 1 - arm, -10, -2, arm, 12, 4, modern ? 32 : 40, modern ? 48 : 16, 0f, texWidth, texHeight);
                    if (modern)
                    {
                        Box(mesh, 1 - arm, -10, -2, arm, 12, 4, 48, 48, 0.25f, texWidth, texHeight);
                    }

                    break;
                case Part.RightLeg:
                    Box(mesh, -2, -12, -2, 4, 12, 4, 0, 16, 0f, texWidth, texHeight);
                    if (modern)
                    {
                        Box(mesh, -2, -12, -2, 4, 12, 4, 0, 32, 0.25f, texWidth, texHeight);
                    }

                    break;
                default:
                    Box(mesh, -2, -12, -2, 4, 12, 4, modern ? 16 : 0, modern ? 48 : 16, 0f, texWidth, texHeight);
                    if (modern)
                    {
                        Box(mesh, -2, -12, -2, 4, 12, 4, 0, 48, 0.25f, texWidth, texHeight);
                    }

                    break;
            }

            _opaque = null;
            return mesh;
        }

        /// <summary>
        /// Old 64x32 skins often fill the unused hat area with a solid colour; Minecraft treats a hat without a single
        /// see-through pixel as no hat at all (its "Notch transparency hack"), and so does this.
        /// </summary>
        private static bool SolidHat(Func<int, int, bool> opaque)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int x = 32; x < 64; x++)
                {
                    if (!opaque(x, y))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// A box from (x, y, z) of size (w, h, d), grown by `grow` on every side, with Minecraft's box unwrap at (u, v):
        /// the top and bottom in the first row, then right side, front, left side and back in the second.
        /// </summary>
        private static void Box(Mesh m, float x, float y, float z, int w, int h, int d, int u, int v, float grow, int tw, int th)
        {
            float x0 = x - grow, x1 = x + w + grow, y0 = y - grow, y1 = y + h + grow, z0 = z - grow, z1 = z + d + grow;
            bool outer = grow > 0f && _opaque != null;
            // each face: corners as seen from outside (top-left, top-right, bottom-right, bottom-left) and its texture
            // rectangle (left, top, width, height) in skin pixels
            Face(m, outer, x1, y1, z1, x0, y1, z1, x0, y0, z1, x1, y0, z1, u + d, v + d, w, h, tw, th);             // front (+z)
            Face(m, outer, x1, y1, z0, x1, y1, z1, x1, y0, z1, x1, y0, z0, u, v + d, d, h, tw, th);                 // right side (+x)
            Face(m, outer, x0, y1, z1, x0, y1, z0, x0, y0, z0, x0, y0, z1, u + d + w, v + d, d, h, tw, th);         // left side (-x)
            Face(m, outer, x0, y1, z0, x1, y1, z0, x1, y0, z0, x0, y0, z0, u + d + w + d, v + d, w, h, tw, th);     // back (-z)
            Face(m, outer, x1, y1, z0, x0, y1, z0, x0, y1, z1, x1, y1, z1, u + d, v, w, d, tw, th);                 // top
            Face(m, outer, x1, y0, z1, x0, y0, z1, x0, y0, z0, x1, y0, z0, u + d + w, v, w, d, tw, th);             // bottom
        }

        [ThreadStatic] private static Func<int, int, bool> _opaque;

        /// <summary>A whole face, or (the outer layer) one square per opaque pixel of its texture rectangle.</summary>
        private static void Face(Mesh m, bool perPixel,
            float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float dx, float dy, float dz,
            int left, int top, int width, int height, int tw, int th)
        {
            if (!perPixel)
            {
                Quad(m, ax, ay, az, bx, by, bz, dx, dy, dz, 0f, 0f, 1f, 1f, left, top, width, height, tw, th);
                return;
            }

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    if (left + i < tw && top + j < th && _opaque(left + i, top + j))
                    {
                        Quad(m, ax, ay, az, bx, by, bz, dx, dy, dz, i / (float)width, j / (float)height, (i + 1) / (float)width, (j + 1) / (float)height,
                            left + i, top + j, 1, 1, tw, th);
                    }
                }
            }
        }

        /// <summary>
        /// The part (s0..s1 across, t0..t1 down) of the face spanned by top-left a, top-right b and bottom-left d, with the
        /// texture rectangle (left, top, width, height).
        /// </summary>
        private static void Quad(Mesh m, float ax, float ay, float az, float bx, float by, float bz, float dx, float dy, float dz,
            float s0, float t0, float s1, float t1, int left, int top, int width, int height, int tw, int th)
        {
            int i = m.VertexCount;
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = dx - ax, vy = dy - ay, vz = dz - az;
            void Corner(float s, float t) => m.Positions.AddRange(new[] { ax + ux * s + vx * t, ay + uy * s + vy * t, az + uz * s + vz * t });
            Corner(s0, t0);
            Corner(s1, t0);
            Corner(s1, t1);
            Corner(s0, t1);
            float u0 = left / (float)tw, u1 = (left + width) / (float)tw;
            float v0 = 1f - top / (float)th, v1 = 1f - (top + height) / (float)th;
            m.Uvs.AddRange(new[] { u0, v0, u1, v0, u1, v1, u0, v1 });
            // clockwise from outside: Unity's front faces
            m.Triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        // ------------------------------------------------------------------ animation

        /// <summary>What drives the pose this frame.</summary>
        public struct Input
        {
            /// <summary>Walk cycle position and strength (0..1), from WalkCycle.</summary>
            public float WalkPosition, WalkAmount;
            /// <summary>0..1 through an arm swing, or negative when not swinging.</summary>
            public float Attack;
            /// <summary>Where the head looks relative to the body: pitch (positive down) and yaw (positive right), radians.</summary>
            public float HeadPitch, HeadYaw;
            /// <summary>Seconds since the model was made (the idle arm sway).</summary>
            public float Age;
            public bool Crouch, Sit, Swim;
            /// <summary>The shield is raised (the off hand, Minecraft's left arm, held up across the chest).</summary>
            public bool Block;
        }

        /// <summary>
        /// The pose: per part (Part order) a rotation x, y, z in radians and an offset x, y, z in pixels from its pivot.
        /// Minecraft's HumanoidModel.setupAnim, without held items.
        /// </summary>
        public static void Animate(Input input, float[] rotation, float[] offset)
        {
            Array.Clear(rotation, 0, PartCount * 3);
            Array.Clear(offset, 0, PartCount * 3);
            float pos = input.WalkPosition, amount = Math.Min(input.WalkAmount, 1f);
            if (input.Swim)
            {
                amount *= 0.6f; // a slow kick rather than a stride
            }

            Set(rotation, Part.Head, input.HeadPitch, input.HeadYaw, 0f);
            Set(rotation, Part.RightArm, Cos(pos * 0.6662f + (float)Math.PI) * 2f * amount * 0.5f, 0f, 0f);
            Set(rotation, Part.LeftArm, Cos(pos * 0.6662f) * 2f * amount * 0.5f, 0f, 0f);
            Set(rotation, Part.RightLeg, Cos(pos * 0.6662f) * 1.4f * amount, 0f, 0f);
            Set(rotation, Part.LeftLeg, Cos(pos * 0.6662f + (float)Math.PI) * 1.4f * amount, 0f, 0f);

            if (input.Sit)
            {
                Add(rotation, Part.RightArm, -(float)Math.PI / 5f, 0f, 0f);
                Add(rotation, Part.LeftArm, -(float)Math.PI / 5f, 0f, 0f);
                Set(rotation, Part.RightLeg, -1.4137167f, (float)Math.PI / 10f, 0.07853982f);
                Set(rotation, Part.LeftLeg, -1.4137167f, -(float)Math.PI / 10f, -0.07853982f);
            }

            if (input.Block)
            {
                int l = (int)Part.LeftArm * 3;
                rotation[l] = rotation[l] * 0.5f - 0.9424779f;
                rotation[l + 1] = (float)Math.PI / 6f;
            }

            // the swing (Minecraft's setupAttackAnimation, right hand)
            if (input.Attack >= 0f)
            {
                float f = Math.Min(input.Attack, 1f);
                float twist = Sin((float)Math.Sqrt(f) * (float)Math.PI * 2f) * 0.2f;
                Set(rotation, Part.Body, 0f, twist, 0f);
                Add(rotation, Part.RightArm, 0f, twist, 0f);
                Add(rotation, Part.LeftArm, twist, twist, 0f);
                // the shoulders follow the body's twist
                offset[(int)Part.RightArm * 3 + 2] = -Sin(twist) * 5f;
                offset[(int)Part.RightArm * 3 + 0] = Cos(twist) * 5f - 5f;
                offset[(int)Part.LeftArm * 3 + 2] = Sin(twist) * 5f;
                offset[(int)Part.LeftArm * 3 + 0] = -Cos(twist) * 5f + 5f;
                float g = 1f - f;
                g = 1f - g * g * g * g;
                float h = Sin(g * (float)Math.PI);
                float k = Sin(f * (float)Math.PI) * -(input.HeadPitch - 0.7f) * 0.75f;
                Add(rotation, Part.RightArm, -(h * 1.2f + k), twist, Sin(f * (float)Math.PI) * -0.4f);
            }

            if (input.Crouch)
            {
                Add(rotation, Part.Body, 0.5f, 0f, 0f);
                Add(rotation, Part.RightArm, 0.4f, 0f, 0f);
                Add(rotation, Part.LeftArm, 0.4f, 0f, 0f);
                // the hips go back, the shoulders and head down
                Offset(offset, Part.RightLeg, 0f, -0.2f, -4f);
                Offset(offset, Part.LeftLeg, 0f, -0.2f, -4f);
                Offset(offset, Part.Body, 0f, -3.2f, 0f);
                Offset(offset, Part.Head, 0f, -4.2f, 0f);
                Offset(offset, Part.RightArm, 0f, -3.2f, 0f);
                Offset(offset, Part.LeftArm, 0f, -3.2f, 0f);
            }

            // idle sway (bobModelPart)
            float age = input.Age * 20f;
            Add(rotation, Part.RightArm, Sin(age * 0.067f) * 0.05f, 0f, Cos(age * 0.09f) * 0.05f + 0.05f);
            Add(rotation, Part.LeftArm, -Sin(age * 0.067f) * 0.05f, 0f, -(Cos(age * 0.09f) * 0.05f + 0.05f));
        }

        private static float Sin(float a) => (float)Math.Sin(a);

        private static float Cos(float a) => (float)Math.Cos(a);

        private static void Set(float[] r, Part p, float x, float y, float z)
        {
            int i = (int)p * 3;
            r[i] = x;
            r[i + 1] = y;
            r[i + 2] = z;
        }

        private static void Add(float[] r, Part p, float x, float y, float z)
        {
            int i = (int)p * 3;
            r[i] += x;
            r[i + 1] += y;
            r[i + 2] += z;
        }

        private static void Offset(float[] o, Part p, float x, float y, float z) => Add(o, p, x, y, z);

        /// <summary>
        /// Minecraft's WalkAnimationState, run continuously: each tick (1/20 s) the strength moves 40% toward how far
        /// the player went (x4, at most 1) and the position advances by the strength.
        /// </summary>
        public sealed class WalkCycle
        {
            public float Position { get; private set; }
            public float Amount { get; private set; }

            /// <summary>`speed`: horizontal metres per second; `dt`: seconds since the last call.</summary>
            public void Step(float speed, float dt)
            {
                if (dt <= 0f)
                {
                    return;
                }

                float ticks = dt * 20f;
                float target = Math.Min(speed / 20f * 4f, 1f);
                Amount += (target - Amount) * (1f - (float)Math.Pow(0.6, ticks));
                Position += Amount * ticks;
                if (Position > 10000f)
                {
                    Position -= 9425.0f; // a whole number of cycles (2 pi / 0.6662 x 999.3), so the stride does not jump
                }
            }
        }

        /// <summary>An arm swing: 0..1 over six ticks; a click in the second half of a swing starts a new one (as in Minecraft).</summary>
        public sealed class Swing
        {
            private const float Duration = 6f / 20f;
            private float _time = -1f;

            public float Progress => _time < 0f ? -1f : _time / Duration;

            public void Start()
            {
                if (_time < 0f || _time >= Duration * 0.5f)
                {
                    _time = 0f;
                }
            }

            public void Step(float dt)
            {
                if (_time < 0f)
                {
                    return;
                }

                _time += dt;
                if (_time >= Duration)
                {
                    _time = -1f;
                }
            }
        }

        /// <summary>
        /// A placeholder skin until Minecraft sends the real one: our own drawing in the classic colours (brown hair,
        /// light skin, teal shirt, blue trousers, grey shoes), RGBA32, rows bottom-up as Unity's Texture2D.SetPixels32 takes them.
        /// </summary>
        public static byte[] PlaceholderSkin()
        {
            const int w = 64, h = 64;
            var px = new byte[w * h * 4];
            void Fill(int u, int v, int width, int height, byte r, byte g, byte b)
            {
                for (int y = v; y < v + height; y++)
                {
                    for (int x = u; x < u + width; x++)
                    {
                        int i = ((h - 1 - y) * w + x) * 4;
                        px[i] = r;
                        px[i + 1] = g;
                        px[i + 2] = b;
                        px[i + 3] = 255;
                    }
                }
            }

            void BoxFill(int u, int v, int bw, int bh, int bd, byte r, byte g, byte b) => Fill(u, v, 2 * (bw + bd), bd + bh, r, g, b);

            // head: skin, hair on top, at the back and a fringe, eyes and a mouth on the face
            BoxFill(0, 0, 8, 8, 8, 0xB5, 0x84, 0x6B);
            Fill(8, 0, 8, 8, 0x3B, 0x26, 0x18);   // top
            Fill(0, 8, 32, 2, 0x3B, 0x26, 0x18);  // fringe all round
            Fill(24, 8, 8, 8, 0x3B, 0x26, 0x18);  // back
            Fill(9, 12, 2, 1, 0xFF, 0xFF, 0xFF);
            Fill(10, 12, 1, 1, 0x52, 0x3D, 0x89);
            Fill(13, 12, 2, 1, 0xFF, 0xFF, 0xFF);
            Fill(13, 12, 1, 1, 0x52, 0x3D, 0x89);
            Fill(11, 14, 2, 1, 0x6A, 0x40, 0x30);
            Fill(10, 15, 4, 1, 0x6A, 0x40, 0x30);
            // body: shirt
            BoxFill(16, 16, 8, 12, 4, 0x00, 0xA8, 0xA8);
            // arms: skin with a short sleeve
            foreach (var a in new[] { (40, 16), (32, 48) })
            {
                BoxFill(a.Item1, a.Item2, 4, 12, 4, 0xB5, 0x84, 0x6B);
                Fill(a.Item1, a.Item2 + 4, 16, 4, 0x00, 0xA8, 0xA8);
            }

            // legs: trousers and shoes
            foreach (var l in new[] { (0, 16), (16, 48) })
            {
                BoxFill(l.Item1, l.Item2, 4, 12, 4, 0x46, 0x3A, 0xA5);
                Fill(l.Item1, l.Item2 + 13, 16, 3, 0x6B, 0x6B, 0x6B);
            }

            return px;
        }
    }
}
