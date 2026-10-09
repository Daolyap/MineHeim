using System;
using System.Globalization;
using System.Threading;
using MineHeim.Core;

namespace MineHeim.Core.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "live")
            {
                return Live(args.Length > 1 ? int.Parse(args[1]) : 25599, args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 8.0);
            }

            OrientationMatchesMinecraftCamera();
            RollSignIsUnambiguous();
            HorizontalYawConvention();
            PositionRoundTrip();
            CamMessageIsCultureInvariant();
            GroundAndBlocksMessages();
            BarrierCellsCountOwners();
            ItemMapPairs();
            CreatureAndMobMessages();
            SteveModelFacesOutward();
            SteveModelSeamsMatchTheSkinLayout();
            SteveModelOuterLayerPerPixel();
            SteveAnimation();
            Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURE(S)");
            return _failures == 0 ? 0 : 1;
        }

        private static void Check(bool ok, string what)
        {
            if (!ok)
            {
                _failures++;
                Console.WriteLine("FAIL: " + what);
            }
        }

        private static double[] Unit(Random r)
        {
            double x, y, z, n;
            do
            {
                x = r.NextDouble() * 2 - 1;
                y = r.NextDouble() * 2 - 1;
                z = r.NextDouble() * 2 - 1;
                n = Math.Sqrt(x * x + y * y + z * z);
            }
            while (n < 0.1 || n > 1.0);
            return new[] { x / n, y / n, z / n };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        /// <summary>
        /// 50,000 random Unity camera orientations (forward + up, any roll): the yaw/pitch/roll we send must make
        /// Minecraft's camera (rotationYXZ(PI - yaw, -pitch, roll)) face the same way, with the same up.
        /// </summary>
        private static void OrientationMatchesMinecraftCamera()
        {
            var rng = new Random(12345);
            double worst = 0;
            int skipped = 0;
            for (int i = 0; i < 50000; i++)
            {
                double[] f = Unit(rng);
                double[] v = Unit(rng);
                double d = Dot(f, v);
                double[] u = { v[0] - d * f[0], v[1] - d * f[1], v[2] - d * f[2] };
                double un = Math.Sqrt(Dot(u, u));
                if (un < 0.05)
                {
                    continue;
                }

                u[0] /= un;
                u[1] /= un;
                u[2] /= un;
                if (Math.Abs(f[1]) > 0.99999)
                {
                    skipped++; // straight up/down: roll is ambiguous, the function returns 0 (covered separately)
                    continue;
                }

                double yaw, pitch, roll;
                Mapping.Orientation(f[0], f[1], f[2], u[0], u[1], u[2], out yaw, out pitch, out roll);
                double[] mf, mu;
                Mapping.MinecraftBasis(yaw, pitch, roll, out mf, out mu);
                // the Unity vectors seen in Minecraft axes: z mirrored
                double e = Math.Max(
                    Math.Max(Math.Abs(mf[0] - f[0]), Math.Max(Math.Abs(mf[1] - f[1]), Math.Abs(mf[2] + f[2]))),
                    Math.Max(Math.Abs(mu[0] - u[0]), Math.Max(Math.Abs(mu[1] - u[1]), Math.Abs(mu[2] + u[2]))));
                worst = Math.Max(worst, e);
            }

            Console.WriteLine("orientation: worst component error over 50000 random cameras = " + worst.ToString("E2", CultureInfo.InvariantCulture) + " (" + skipped + " near-vertical skipped)");
            Check(worst < 1e-9, "orientation worst error " + worst);
        }

        /// <summary>A roll of +20 degrees must differ from -20: guards against a sign flip hiding in a symmetric case.</summary>
        private static void RollSignIsUnambiguous()
        {
            // camera facing Unity +z (north), pitched 10 degrees down, rolled: build forward/up from the Minecraft side and map back
            foreach (double rollIn in new[] { 20.0, -20.0, 75.0 })
            {
                double[] mf, mu;
                Mapping.MinecraftBasis(30.0, 10.0, rollIn, out mf, out mu);
                // Minecraft vectors to Unity vectors: mirror z
                double yaw, pitch, roll;
                Mapping.Orientation(mf[0], mf[1], -mf[2], mu[0], mu[1], -mu[2], out yaw, out pitch, out roll);
                Console.WriteLine("roll " + rollIn + " -> yaw " + yaw.ToString("0.00", CultureInfo.InvariantCulture) + " pitch " + pitch.ToString("0.00", CultureInfo.InvariantCulture) + " roll " + roll.ToString("0.00", CultureInfo.InvariantCulture));
                Check(Math.Abs(yaw - 30.0) < 1e-6 && Math.Abs(pitch - 10.0) < 1e-6 && Math.Abs(roll - rollIn) < 1e-6, "round trip of yaw 30 pitch 10 roll " + rollIn);
            }
        }

        /// <summary>Compass check: Valheim's +z (north) becomes Minecraft's -z (also north), which is yaw 180.</summary>
        private static void HorizontalYawConvention()
        {
            // Unity +z (north)  -> Minecraft -z (north) -> yaw 180
            // Unity +x (east)   -> Minecraft +x (east)  -> yaw -90 (Minecraft yaw 270 / -90 faces +x)
            // Unity -z (south)  -> Minecraft +z (south) -> yaw 0
            // Unity -x (west)   -> Minecraft -x (west)  -> yaw 90
            Check(Math.Abs(Math.Abs(Mapping.Yaw(0, 1)) - 180) < 1e-9, "north is yaw 180, got " + Mapping.Yaw(0, 1));
            Check(Math.Abs(Mapping.Yaw(1, 0) - -90) < 1e-9, "east is yaw -90, got " + Mapping.Yaw(1, 0));
            Check(Math.Abs(Mapping.Yaw(0, -1)) < 1e-9, "south is yaw 0, got " + Mapping.Yaw(0, -1));
            Check(Math.Abs(Mapping.Yaw(-1, 0) - 90) < 1e-9, "west is yaw 90, got " + Mapping.Yaw(-1, 0));
            // and the full-orientation function agrees with Yaw() for a level camera
            double yaw, pitch, roll;
            Mapping.Orientation(1, 0, 0, 0, 1, 0, out yaw, out pitch, out roll);
            Check(Math.Abs(yaw - -90) < 1e-9 && Math.Abs(pitch) < 1e-9 && Math.Abs(roll) < 1e-9, "level east camera: " + yaw + "/" + pitch + "/" + roll);
        }

        private static void PositionRoundTrip()
        {
            double mx, my, mz, x, y, z;
            Mapping.Position(12.5, 31.25, -40.0, 68.75, out mx, out my, out mz);
            Check(mx == 12.5 && my == 100.0 && mz == 40.0, "position mapping " + mx + "," + my + "," + mz);
            Mapping.FromMinecraft(mx, my, mz, 68.75, out x, out y, out z);
            Check(x == 12.5 && y == 31.25 && z == -40.0, "position round trip");
        }

        private static void CamMessageIsCultureInvariant()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma
                string s = Messages.Cam(7, 1.5, 100.25, -3.125, 180.0, 12.5, 0.0, 65.0, true, 1.5, 99.0, -3.0, 175.0);
                Console.WriteLine(s);
                Check(!s.Contains("1,5") && s.Contains("[1.5,100.25,-3.125]") && s.Contains("\"f\":7") && s.Contains("\"fp\":true"), "cam message formatting: " + s);
                Check(Messages.Command("say \"hi\"") == "{\"t\":\"cmd\",\"c\":\"say \\\"hi\\\"\"}", "command escaping");
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        private static void GroundAndBlocksMessages()
        {
            var nums = Messages.Numbers("{\"t\":\"infect\",\"l\":[[1,-2.5,103,4.25,1e1],[2,0,0,0,0]],\"mobs\":3}", "l");
            Check(nums.Count == 10 && nums[1] == -2.5 && nums[4] == 10 && nums[5] == 2, "nested numbers: " + string.Join(",", nums));
            Check(Messages.Numbers("{\"t\":\"infect\",\"l\":[],\"mobs\":3}", "l").Count == 0 && Messages.Numbers("{}", "l").Count == 0, "empty numbers");
            var add = new System.Collections.Generic.List<int> { -68, 192, 99, 100, 5, -7, 0, 0 };
            var rem = new System.Collections.Generic.List<int> { 1, 2, 3, 4 };
            string g = Messages.Ground(add, rem);
            Check(g == "{\"t\":\"ground\",\"c\":[-68,192,99,100,5,-7,0,0],\"r\":[1,2,3,4]}", "ground message: " + g);
            Check(Messages.Ground(new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<int>()) == "{\"t\":\"ground\",\"c\":[],\"r\":[]}", "empty ground message");

            string blocks = "{\"t\":\"blocks\",\"set\":[0,100,5,-12,64,-3],\"clear\":[7,8,9]}";
            var set = Messages.IntArray(blocks, "set");
            var clear = Messages.IntArray(blocks, "clear");
            Check(set.Count == 6 && set[0] == 0 && set[1] == 100 && set[3] == -12 && set[5] == -3, "blocks set parse: " + string.Join(",", set));
            Check(clear.Count == 3 && clear[2] == 9, "blocks clear parse");
            Check(Messages.IntArray("{\"t\":\"blocks\",\"set\":[],\"clear\":[]}", "set").Count == 0, "empty array");
            Check(Messages.IntArray(blocks, "nope").Count == 0, "missing key");
            Check(Messages.Type(blocks) == "blocks" && Messages.Type("{\"t\":\"probe\",\"r\":[1]}") == "probe", "type");
            Check(Messages.IntValue("{\"t\":\"selected\",\"n\":7}", "n", -1) == 7 && Messages.IntValue("{\"n\":-3}", "n", 0) == -3 && Messages.IntValue("{}", "n", 42) == 42, "IntValue");
            string proj = "{\"t\":\"proj\",\"p\":[[12,\"arrow\",-1.500,64.250,3.000],[13,\"firework\",0.000,70.000,-2.250]]}";
            var pl = Messages.Projectiles(proj);
            Check(pl.Count == 2 && pl[0].Id == 12 && pl[0].Kind == "arrow" && pl[0].X == -1.5 && pl[0].Y == 64.25 && pl[1].Kind == "firework" && pl[1].Z == -2.25, "projectiles parse");
            double ex, ey, ez, er; string es;
            Check(Messages.Explosion("{\"t\":\"explosion\",\"pos\":[2.500,64.061,8.500],\"r\":4.00,\"src\":\"tnt\"}", out ex, out ey, out ez, out er, out es) && ex == 2.5 && ey == 64.061 && ez == 8.5 && er == 4.0 && es == "tnt", "explosion parse");
            Check(Messages.Explosion("{\"t\":\"explosion\",\"pos\":[1.0,2.0,3.0],\"r\":3.0}", out ex, out ey, out ez, out er, out es) && es == "", "explosion without source");
            Check(Messages.StringValue("{\"t\":\"melee\",\"item\":\"diamond_pickaxe\"}", "item", "") == "diamond_pickaxe" && Messages.StringValue("{}", "item", "x") == "x", "StringValue");
            const string hurt = "{\"t\":\"hurt\",\"amount\":3.50,\"src\":\"zombie\",\"pos\":[1.000,2.000,3.000]}";
            Check(Messages.DoubleValue(hurt, "amount", -1) == 3.5 && Messages.StringValue(hurt, "src", "") == "zombie", "hurt message parse");
            Check(Messages.DoubleValue("{\"t\":\"x\",\"a\":-1.5e2}", "a", 0) == -150 && Messages.DoubleValue("{\"t\":\"x\"}", "a", 7) == 7, "double value: exponent and fallback");
            Check(Messages.BoolValue("{\"t\":\"shield\",\"up\":true}", "up", false) && !Messages.BoolValue("{\"t\":\"shield\",\"up\":false}", "up", true) && Messages.BoolValue("{\"t\":\"shield\"}", "up", true), "bool value");
            Check(Messages.GameMode(true) == "{\"t\":\"gamemode\",\"m\":\"survival\"}" && Messages.GameMode(false).Contains("creative"), "gamemode message");
            Check(Messages.ProjectileHit(5, 1.5, 2.25, -3, true) =="{\"t\":\"projhit\",\"id\":5,\"pos\":[1.5,2.25,-3],\"stick\":true}", "projhit message: " + Messages.ProjectileHit(5, 1.5, 2.25, -3, true));
            Check(Messages.Probe(new System.Collections.Generic.List<int> { 1, 2, 3 }) == "{\"t\":\"probe\",\"c\":[1,2,3]}", "probe message");
            Check(Messages.ItemIn("minecraft:oak_log", 5, 1.5, 100, -2, 0.1, 0.2, 0) == "{\"t\":\"itemin\",\"item\":\"minecraft:oak_log\",\"n\":5,\"pos\":[1.5,100,-2],\"vel\":[0.1,0.2,0]}",
                "itemin message: " + Messages.ItemIn("minecraft:oak_log", 5, 1.5, 100, -2, 0.1, 0.2, 0));
            Check(Messages.ItemTake(42, 4) == "{\"t\":\"itemtake\",\"id\":42,\"n\":4}", "itemtake message");
            Check(Messages.DayTime(6000) == "{\"t\":\"daytime\",\"v\":6000}", "daytime message");
            Check(Messages.DayTicks(0.5) == 6000 && Messages.DayTicks(0.25) == 0 && Messages.DayTicks(0.0) == 18000 && Messages.DayTicks(0.75) == 12000 && Messages.DayTicks(1.0) == 18000,
                "day fraction -> ticks: noon " + Messages.DayTicks(0.5) + ", sunrise " + Messages.DayTicks(0.25) + ", midnight " + Messages.DayTicks(0.0));
            string itemout = "{\"t\":\"itemout\",\"id\":17,\"item\":\"minecraft:oak_planks\",\"n\":6,\"pos\":[1.000,100.000,2.000]}";
            Check(Messages.IntValue(itemout, "id", -1) == 17 && Messages.StringValue(itemout, "item", "") == "minecraft:oak_planks" && Messages.IntValue(itemout, "n", 0) == 6, "itemout parse");
            Check(Messages.ValheimLights(new System.Collections.Generic.List<int> { 1, 100, 2, 15 }, new System.Collections.Generic.List<int>()) == "{\"t\":\"vlights\",\"c\":[1,100,2,15],\"r\":[]}", "vlights message");
        }

        private static void CreatureAndMobMessages()
        {
            var creatures = new System.Collections.Generic.List<Messages.Creature>
            {
                new Messages.Creature { Id = -12, Name = "Greydwarf", X = 1.5, Y = 100, Z = -3.25, Width = 0.9, Height = 1.85 },
                new Messages.Creature { Id = 7, Name = "Troll\"x", X = 0, Y = 0, Z = 0, Width = 2.5, Height = 5 },
            };
            string c = Messages.Creatures(creatures);
            Check(c == "{\"t\":\"creatures\",\"c\":[[-12,\"Greydwarf\",1.5,100,-3.25,0.9,1.85],[7,\"Trollx\",0,0,0,2.5,5]]}", "creatures message (names made JSON-safe): " + c);
            Check(Messages.Creatures(new System.Collections.Generic.List<Messages.Creature>()) == "{\"t\":\"creatures\",\"c\":[]}", "no creatures");

            var mobs = Messages.Mobs("{\"t\":\"mobs\",\"m\":[[51,\"zombie\",1.000,100.000,-2.500,0.60,1.95,17.0,20.0,1],[52,\"cow\",3.000,100.000,4.000,0.90,1.40,10.0,10.0,0]]}");
            Check(mobs.Count == 2 && mobs[0].Id == 51 && mobs[0].Type == "zombie" && mobs[0].Z == -2.5 && mobs[0].Height == 1.95 && mobs[0].Health == 17 && mobs[0].MaxHealth == 20 && mobs[0].Hostile
                  && mobs[1].Type == "cow" && !mobs[1].Hostile, "mobs parse");
            Check(Messages.Mobs("{\"t\":\"mobs\",\"m\":[]}").Count == 0, "no mobs");
            Check(Messages.MobHit(51, 2.5, -1, true) == "{\"t\":\"mhit\",\"id\":51,\"amount\":2.5,\"by\":-1,\"player\":true}", "mhit message: " + Messages.MobHit(51, 2.5, -1, true));
            Check(Messages.HealthPerPoint(100) == 5 && Messages.HealthPerPoint(25) == 1.25 && Messages.HealthPerPoint(10) == 1.25, "exchange rate: 100 health -> 5 per point, never under 25/20");
            var pl = Messages.Projectiles("{\"t\":\"proj\",\"p\":[[12,\"arrow\",-1.500,64.250,3.000,9.0],[13,\"firework\",0.000,70.000,-2.250]]}");
            Check(pl.Count == 2 && pl[0].Damage == 9 && pl[1].Damage == -1 && pl[1].Z == -2.25, "projectile damage parse (optional)");
            const string chit = "{\"t\":\"chit\",\"id\":-12,\"amount\":3.00,\"src\":\"zombie\",\"by\":51,\"ranged\":false,\"pos\":[1.000,100.000,-2.500]}";
            Check(Messages.IntValue(chit, "id", 0) == -12 && Messages.DoubleValue(chit, "amount", 0) == 3 && Messages.IntValue(chit, "by", -1) == 51 && !Messages.BoolValue(chit, "ranged", true), "chit parse");
        }

        /// <summary>Two owners of one cell: only the first sends it and only the last clears it; runs merge into columns.</summary>
        private static void BarrierCellsCountOwners()
        {
            var cells = new BarrierCells();
            var add = new System.Collections.Generic.List<int>();
            var rem = new System.Collections.Generic.List<int>();
            cells.Add(3, 99, -4);
            cells.Add(3, 100, -4);
            cells.Add(3, 101, -4);
            cells.Add(3, 103, -4);
            cells.Add(-70, -5, 12);
            Check(cells.Drain(add, rem) && string.Join(",", add) == "-70,12,-5,-5,3,-4,99,101,3,-4,103,103" && rem.Count == 0, "barrier runs merged: " + string.Join(",", add));
            add.Clear();
            Check(!cells.Drain(add, rem), "nothing new: no message");

            cells.Add(3, 100, -4); // a second owner (a floor on the ground)
            Check(!cells.Drain(add, rem), "a second owner sends nothing");
            cells.Remove(3, 100, -4);
            Check(!cells.Drain(add, rem) && cells.Has(3, 100, -4), "the first owner letting go keeps the cell");
            cells.Remove(3, 100, -4);
            Check(cells.Drain(add, rem) && add.Count == 0 && string.Join(",", rem) == "3,-4,100,100", "the last owner letting go clears it: " + string.Join(",", rem));
            rem.Clear();

            cells.Add(0, 0, 0);
            cells.Remove(0, 0, 0);
            Check(!cells.Drain(add, rem), "added and removed between drains: nothing sent");
            cells.Remove(3, 101, -4);
            cells.Add(3, 101, -4);
            Check(!cells.Drain(add, rem), "removed and added back between drains: nothing sent");
            cells.Forget(3, 103, -4);
            Check(!cells.Drain(add, rem) && !cells.Has(3, 103, -4), "forget sends no clear");
            cells.Remove(77, 1, 1);
            Check(!cells.Drain(add, rem), "removing a cell nobody owns is a no-op");
            int x, y, z;
            BarrierCells.Unpack(BarrierCells.Key(-33554432, -512, 33554431), out x, out y, out z);
            Check(x == -33554432 && y == -512 && z == 33554431, "key round trip at the extremes");
            cells.Reset();
            Check(cells.Count == 0 && !cells.Drain(add, rem), "reset forgets everything");
        }

        private static void ItemMapPairs()
        {
            var map = new ItemMap();
            string mc;
            int n, used;
            string v;
            Check(map.ToMinecraft("Wood", 7, out mc, out n) && mc == "minecraft:oak_log" && n == 7, "Wood -> oak_log");
            Check(!map.ToMinecraft("SwordIron", 1, out mc, out n), "an iron sword has no Minecraft twin");
            Check(!map.ToMinecraft("Wood", 0, out mc, out n), "nothing to send");
            Check(map.ToValheim("minecraft:oak_log", 3, out v, out n, out used) && v == "Wood" && n == 3 && used == 3, "oak_log -> Wood");
            Check(map.ToValheim("birch_log", 2, out v, out n, out used) && v == "FineWood", "birch_log -> FineWood (prefix optional)");
            Check(map.ToValheim("minecraft:jungle_log", 2, out v, out n, out used) && v == "Wood", "an alias: jungle_log -> Wood");
            Check(map.ToValheim("minecraft:oak_planks", 6, out v, out n, out used) && v == "Wood" && n == 1 && used == 4, "6 planks: 4 cross as 1 Wood, 2 stay");
            Check(!map.ToValheim("minecraft:oak_planks", 3, out v, out n, out used), "3 planks: too few to cross");
            Check(!map.ToValheim("minecraft:diamond_sword", 1, out v, out n, out used), "a diamond sword stays in Minecraft");
            Check(map.ToMinecraft("Coal", 1, out mc, out n) && mc == "minecraft:coal", "the alias charcoal does not change Coal's way into Minecraft");

            var problems = new System.Collections.Generic.List<string>();
            map.Apply("Wood=minecraft:spruce_log, Tin=iron_nugget*2, bad, Stone=cobblestone*0", problems);
            Check(map.ToMinecraft("Wood", 1, out mc, out n) && mc == "minecraft:spruce_log", "the player's pairs replace the defaults");
            Check(map.ToMinecraft("Tin", 3, out mc, out n) && mc == "minecraft:iron_nugget" && n == 6 && map.ToValheim("iron_nugget", 5, out v, out n, out used) && v == "Tin" && n == 2 && used == 4,
                "a new pair with a ratio both ways");
            Check(problems.Count == 2, "bad entries are reported: " + string.Join(" | ", problems));
            Check(new ItemMap().Count >= 40, "the default table has every pair: " + new ItemMap().Count);
        }

        // ------------------------------------------------------------------ Steve's model (the host draws him itself)

        private static float[] V(SteveModel.Mesh m, int vertex)
        {
            return new[] { m.Positions[vertex * 3], m.Positions[vertex * 3 + 1], m.Positions[vertex * 3 + 2] };
        }

        private static bool Same(float[] a, float[] b)
        {
            return Math.Abs(a[0] - b[0]) < 1e-4 && Math.Abs(a[1] - b[1]) < 1e-4 && Math.Abs(a[2] - b[2]) < 1e-4;
        }

        /// <summary>Every triangle of every part winds clockwise seen from outside (Unity's front faces): its normal points away from the box.</summary>
        private static void SteveModelFacesOutward()
        {
            foreach (bool slim in new[] { false, true })
            {
                for (int p = 0; p < SteveModel.PartCount; p++)
                {
                    var m = SteveModel.Build((SteveModel.Part)p, slim, 64, 64);
                    // the base box comes first: 6 faces, 4 vertices each; its centre is the mean of its 24 vertices
                    double cx = 0, cy = 0, cz = 0;
                    for (int v = 0; v < 24; v++)
                    {
                        var q = V(m, v);
                        cx += q[0] / 24;
                        cy += q[1] / 24;
                        cz += q[2] / 24;
                    }

                    Check(m.VertexCount == 48 && m.Triangles.Count == 72, "part " + p + ": two whole boxes, got " + m.VertexCount + " vertices");
                    int inward = 0;
                    for (int t = 0; t < 12 * 3; t += 3)
                    {
                        var a = V(m, m.Triangles[t]);
                        var b = V(m, m.Triangles[t + 1]);
                        var c = V(m, m.Triangles[t + 2]);
                        double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                        double mx = (a[0] + b[0] + c[0]) / 3 - cx, my = (a[1] + b[1] + c[1]) / 3 - cy, mz = (a[2] + b[2] + c[2]) / 3 - cz;
                        if (nx * mx + ny * my + nz * mz <= 0)
                        {
                            inward++;
                        }
                    }

                    Check(inward == 0, "part " + p + (slim ? " (slim)" : "") + ": " + inward + " triangle(s) face inward");
                }
            }
        }

        /// <summary>
        /// Minecraft's unwrap: in the skin, the right side's right edge meets the front's left edge, the front's right edge the
        /// left side's left edge, the left side's right edge the back's left edge, and the top's bottom edge the front's top.
        /// The model's matching corners must be the same points. Face order: front, right, left, back, top, bottom;
        /// corners: top-left, top-right, bottom-right, bottom-left (texture).
        /// </summary>
        private static void SteveModelSeamsMatchTheSkinLayout()
        {
            var m = SteveModel.Build(SteveModel.Part.Head, false, 64, 64);
            int Face(int f, int corner) => f * 4 + corner;
            const int front = 0, right = 1, left = 2, back = 3, top = 4;
            Check(Same(V(m, Face(right, 1)), V(m, Face(front, 0))) && Same(V(m, Face(right, 2)), V(m, Face(front, 3))), "right side meets the front");
            Check(Same(V(m, Face(front, 1)), V(m, Face(left, 0))) && Same(V(m, Face(front, 2)), V(m, Face(left, 3))), "front meets the left side");
            Check(Same(V(m, Face(left, 1)), V(m, Face(back, 0))) && Same(V(m, Face(left, 2)), V(m, Face(back, 3))), "left side meets the back");
            Check(Same(V(m, Face(top, 3)), V(m, Face(front, 0))) && Same(V(m, Face(top, 2)), V(m, Face(front, 1))), "top meets the front");
            // the face (skin 8..16 x 8..16) is on the front (+z), the character's right (texture left) at +x
            var tl = V(m, Face(front, 0));
            Check(tl[0] == 4 && tl[1] == 8 && tl[2] == 4, "head front top-left corner: " + string.Join(",", tl));
            Check(Math.Abs(m.Uvs[0] - 8 / 64f) < 1e-6 && Math.Abs(m.Uvs[1] - (1 - 8 / 64f)) < 1e-6, "head front uv starts at skin (8, 8)");
            // a right arm hangs at +x, its outer side (skin 40..44 x 20..32) facing +x
            float px, py, pz;
            SteveModel.Pivot(SteveModel.Part.RightArm, false, out px, out py, out pz);
            var arm = SteveModel.Build(SteveModel.Part.RightArm, false, 64, 64);
            Check(px > 0 && V(arm, Face(right, 0))[0] == 3 && Math.Abs(arm.Uvs[right * 8] - 40 / 64f) < 1e-6, "right arm: at +x, outer side textured from (40, 20)");
            // an old 64x32 skin: only the hat as an outer layer, the left limbs borrow the right limbs' pixels
            var oldLeg = SteveModel.Build(SteveModel.Part.LeftLeg, false, 64, 32);
            Check(oldLeg.VertexCount == 24 && Math.Abs(oldLeg.Uvs[1] - (1 - 20 / 32f)) < 1e-6, "64x32 skin: left leg is one box textured from the right leg's");
            // an old skin whose hat area is solid (no see-through pixel) has no hat, as in Minecraft; a modern one keeps it
            Check(SteveModel.Build(SteveModel.Part.Head, false, 64, 32, (x, y) => true).VertexCount == 24, "64x32 skin with a solid hat area: no hat");
            Check(SteveModel.Build(SteveModel.Part.Head, false, 64, 32, (x, y) => !(x == 63 && y == 15)).VertexCount > 24, "64x32 skin with a real hat keeps it");
            Check(SteveModel.Build(SteveModel.Part.Head, false, 64, 64, (x, y) => true).VertexCount > 24, "64x64 skin with a solid hat keeps it");
        }

        private static void SteveModelOuterLayerPerPixel()
        {
            // only the hat's front top-left pixel (skin 40, 8) is there: one square, at the front's top corner on the right (+x)
            var m = SteveModel.Build(SteveModel.Part.Head, false, 64, 64, (x, y) => x == 40 && y == 8);
            Check(m.VertexCount == 24 + 4, "one opaque hat pixel gives one square, got " + (m.VertexCount - 24) + " vertices");
            var c = V(m, 24);
            Check(Math.Abs(c[0] - 4.5) < 1e-4 && Math.Abs(c[1] - 8.5) < 1e-4 && Math.Abs(c[2] - 4.5) < 1e-4, "hat pixel corner: " + string.Join(",", c));
            var none = SteveModel.Build(SteveModel.Part.Body, false, 64, 64, (x, y) => false);
            Check(none.VertexCount == 24, "a transparent jacket adds nothing");
            byte[] skin = SteveModel.PlaceholderSkin();
            Check(skin.Length == 64 * 64 * 4 && skin[((63 - 8) * 64 + 8) * 4 + 3] == 255, "placeholder skin is 64x64 with an opaque face");
        }

        private static void SteveAnimation()
        {
            var walk = new SteveModel.WalkCycle();
            for (int i = 0; i < 120; i++)
            {
                walk.Step(4.3f, 1f / 60f);
            }

            Check(Math.Abs(walk.Amount - 0.86f) < 0.01f && walk.Position > 20f, "walking at 4.3 m/s: stride " + walk.Amount + ", position " + walk.Position);
            for (int i = 0; i < 120; i++)
            {
                walk.Step(0f, 1f / 60f);
            }

            Check(walk.Amount < 0.01f, "standing still: the stride dies away, got " + walk.Amount);

            var swing = new SteveModel.Swing();
            Check(swing.Progress < 0f, "no swing yet");
            swing.Start();
            swing.Step(0.15f);
            Check(Math.Abs(swing.Progress - 0.5f) < 1e-4, "half way after 3 ticks: " + swing.Progress);
            swing.Step(0.16f);
            Check(swing.Progress < 0f, "the swing ends after 6 ticks");

            var rot = new float[SteveModel.PartCount * 3];
            var off = new float[SteveModel.PartCount * 3];
            SteveModel.Animate(new SteveModel.Input { Attack = 0.4f }, rot, off);
            Check(rot[(int)SteveModel.Part.RightArm * 3] < -0.9f, "mid-swing the right arm is raised forward: " + rot[(int)SteveModel.Part.RightArm * 3]);
            SteveModel.Animate(new SteveModel.Input { Crouch = true, HeadPitch = 0.3f, HeadYaw = -0.2f }, rot, off);
            Check(rot[(int)SteveModel.Part.Body * 3] == 0.5f && off[(int)SteveModel.Part.Head * 3 + 1] < -4f && rot[0] == 0.3f && rot[1] == -0.2f, "crouching and looking");
            SteveModel.Animate(new SteveModel.Input { WalkPosition = 0f, WalkAmount = 1f }, rot, off);
            Check(rot[(int)SteveModel.Part.RightLeg * 3] > 1.3f && rot[(int)SteveModel.Part.LeftLeg * 3] < -1.3f, "full stride: the legs swing apart");
        }

        /// <summary>
        /// Against a running Minecraft (gradlew runClient): connect with WsClient, expect "hello", stream 60 Hz cameras
        /// with a frame counter for a few seconds, ask for a gold column, and print what comes back.
        /// </summary>
        private static int Live(int port, double seconds)
        {
            var ws = new WsClient("127.0.0.1", port);
            ws.Log += m => Console.WriteLine("[ws] " + m);
            ws.Start();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool hello = false;
            long frame = 0;
            int events = 0;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                string m;
                while (ws.TryDequeue(out m))
                {
                    events++;
                    if (m.Contains("\"hello\""))
                    {
                        hello = true;
                        ws.Send(Messages.View(960, 540));
                    }

                    Console.WriteLine("[mc] " + (m.Length > 160 ? m.Substring(0, 160) + "..." : m));
                }

                if (ws.Connected)
                {
                    frame++;
                    double a = sw.Elapsed.TotalSeconds * 0.5;
                    ws.SendLatest(Messages.Cam(frame, 8 * Math.Sin(a), 101.7, 8 * Math.Cos(a) + 5, 180 - a * Mapping.Deg % 360, 15, 0, 60, true, 8 * Math.Sin(a), 100, 8 * Math.Cos(a) + 5, 0));
                    if (frame == 30)
                    {
                        ws.Send(Messages.Command("setblock 0 100 5 minecraft:gold_block"));
                    }
                }

                Thread.Sleep(16);
            }

            Console.WriteLine("live: hello=" + hello + " frames sent=" + frame + " messages received=" + events);
            ws.Dispose();
            return hello && frame > 0 ? 0 : 1;
        }
    }
}
