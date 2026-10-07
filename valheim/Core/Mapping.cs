using System;

namespace MineHeim.Core
{
    /// <summary>
    /// Valheim (Unity) world to Minecraft world. 1 metre = 1 block.
    ///
    /// Unity is left-handed: x east, y up, z north (forward). Minecraft is right-handed: x east, y up, z south.
    /// So a point maps to (x, y + yOffset, -z): the same up axis, and z mirrored. A direction maps the same way
    /// without the offset. Minecraft yaw 0 faces +z (south), 90 faces -x (west); pitch is positive looking down.
    /// Roll is the camera's rotation about its forward axis.
    ///
    /// No UnityEngine types here, so the maths is tested in Core.Tests without the game.
    /// </summary>
    public static class Mapping
    {
        public const double Deg = 180.0 / Math.PI;

        /// <summary>Valheim point to Minecraft point.</summary>
        public static void Position(double x, double y, double z, double yOffset, out double mx, out double my, out double mz)
        {
            mx = x;
            my = y + yOffset;
            mz = -z;
        }

        /// <summary>Minecraft point back to Valheim.</summary>
        public static void FromMinecraft(double mx, double my, double mz, double yOffset, out double x, out double y, out double z)
        {
            x = mx;
            y = my - yOffset;
            z = -mz;
        }

        /// <summary>Minecraft yaw (degrees) of a horizontal facing given as a Unity direction. Vertical-only input gives 0.</summary>
        public static double Yaw(double fx, double fz)
        {
            // Minecraft forward is (-sin yaw, 0, cos yaw) in (x, z); the Unity direction has its z mirrored
            return Math.Atan2(-fx, -fz) * Deg;
        }

        /// <summary>
        /// A Unity camera basis (forward and up, both unit length, in Unity world space) to Minecraft yaw, pitch and
        /// roll in degrees: the rotation that Minecraft's camera builds with rotationYXZ(PI - yaw, -pitch, roll)
        /// then has the same forward and up (after the z mirror).
        /// </summary>
        public static void Orientation(double fx, double fy, double fz, double ux, double uy, double uz,
                                       out double yaw, out double pitch, out double roll)
        {
            // into Minecraft axes
            fz = -fz;
            uz = -uz;

            yaw = Math.Atan2(-fx, fz) * Deg;
            pitch = -Math.Asin(Math.Max(-1.0, Math.Min(1.0, fy))) * Deg;

            // roll: angle about forward from the roll-free up (vertical plane through forward) to the actual up
            // left0 = worldUp x forward, up0 = forward x left0; Minecraft's camera has left = up x forward
            double lx = fz, ly = 0.0, lz = -fx; // (0,1,0) x (fx,fy,fz) = (fz, 0, -fx)
            double ll = Math.Sqrt(lx * lx + lz * lz);
            if (ll < 1e-9)
            {
                roll = 0.0; // looking straight up or down: roll is not separable from yaw
                return;
            }

            lx /= ll;
            lz /= ll;
            double u0x = fy * lz - fz * ly, u0y = fz * lx - fx * lz, u0z = fx * ly - fy * lx; // forward x left0
            double dot = u0x * ux + u0y * uy + u0z * uz;
            double cx = u0y * uz - u0z * uy, cy = u0z * ux - u0x * uz, cz = u0x * uy - u0y * ux; // up0 x up
            double sin = cx * fx + cy * fy + cz * fz;
            // positive Minecraft roll turns the up vector the other way round forward (found by Core.Tests, not assumed)
            roll = -Math.Atan2(sin, dot) * Deg;
        }

        /// <summary>
        /// The camera basis Minecraft builds from yaw/pitch/roll (degrees): same maths as Camera#setRotation
        /// (JOML rotationYXZ(PI - yaw, -pitch, roll)) applied to FORWARDS (0,0,-1) and UP (0,1,0). For tests.
        /// </summary>
        public static void MinecraftBasis(double yaw, double pitch, double roll,
                                          out double[] forward, out double[] up)
        {
            double ay = Math.PI - yaw / Deg, ax = -pitch / Deg, az = roll / Deg;
            forward = RotateYXZ(ay, ax, az, 0, 0, -1);
            up = RotateYXZ(ay, ax, az, 0, 1, 0);
        }

        private static double[] RotateYXZ(double ay, double ax, double az, double x, double y, double z)
        {
            // v' = Ry(ay) * Rx(ax) * Rz(az) * v   (right-handed, counter-clockwise positive)
            double c = Math.Cos(az), s = Math.Sin(az);
            double x1 = c * x - s * y, y1 = s * x + c * y, z1 = z;
            c = Math.Cos(ax);
            s = Math.Sin(ax);
            double x2 = x1, y2 = c * y1 - s * z1, z2 = s * y1 + c * z1;
            c = Math.Cos(ay);
            s = Math.Sin(ay);
            return new[] { c * x2 + s * z2, y2, -s * x2 + c * z2 };
        }
    }
}
