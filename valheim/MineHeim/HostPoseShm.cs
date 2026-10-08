using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MineHeim
{
    /// <summary>
    /// Valheim's camera pose per frame, in Minecraft coordinates, in the named shared memory "Local\MineHeimHostPose".
    /// The ReShade add-on (same process) reads it at Present to composite Minecraft's frame against the pose that frame
    /// was actually rendered with.
    ///
    /// <pre>
    /// header: 0 u32 magic "VCHP" (0x50484356)  4 u32 version (1)  8 u32 slots (8)  16 i64 poses written
    /// slot i at 64 + 128 * i:
    ///   +0 i64 seq (odd while being written)  +8 i64 frame (the number sent in the "cam" message)  +16 i64 QPC time
    ///   +24 f64 x  +32 f64 y  +40 f64 z  (camera, Minecraft coordinates)
    ///   +48 f32 yaw  +52 f32 pitch  +56 f32 roll  +60 f32 vertical fov (degrees)
    ///   +64 f32 near  +68 f32 far   +72 i32 flags: 1 = in a world, 2 = first person, 4 = a Valheim menu is open, 8 = Minecraft attached
    ///   +76 f32 Nether heat at the camera (0..1)  +80 f32 the End at the camera (0..1)
    /// </pre>
    /// </summary>
    internal static unsafe class HostPoseShm
    {
        public const string Name = "Local\\MineHeimHostPose";
        private const int Header = 64, Slots = 8, Stride = 128;
        private const uint PageReadWrite = 0x04, FileMapAllAccess = 0xF001F;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        private static byte* _base;
        private static long _written;

        public static bool Ready => _base != null;

        public static void Init()
        {
            if (_base != null)
            {
                return;
            }

            int size = Header + Slots * Stride;
            IntPtr mapping = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, PageReadWrite, 0, (uint)size, Name);
            if (mapping == IntPtr.Zero)
            {
                Plugin.Log.LogWarning("host pose: CreateFileMapping failed (" + Marshal.GetLastWin32Error() + ")");
                return;
            }

            IntPtr view = MapViewOfFile(mapping, FileMapAllAccess, 0, 0, new UIntPtr((uint)size));
            if (view == IntPtr.Zero)
            {
                Plugin.Log.LogWarning("host pose: MapViewOfFile failed (" + Marshal.GetLastWin32Error() + ")");
                return;
            }

            _base = (byte*)view;
            *(uint*)(_base + 0) = 0x50484356;
            *(uint*)(_base + 4) = 1;
            *(uint*)(_base + 8) = Slots;
            Plugin.Log.LogInfo("host pose: shared memory " + Name + " ready");
        }

        public static void Write(long frame, double x, double y, double z, float yaw, float pitch, float roll, float fov,
                                 float near, float far, int flags, long qpc, float heat = 0f, float end = 0f)
        {
            if (_base == null)
            {
                return;
            }

            byte* slot = _base + Header + (int)(_written % Slots) * Stride;
            long seq = *(long*)slot;
            if ((seq & 1) != 0)
            {
                seq++;
            }

            *(long*)slot = seq + 1;
            Thread.MemoryBarrier();
            *(long*)(slot + 8) = frame;
            *(long*)(slot + 16) = qpc;
            *(double*)(slot + 24) = x;
            *(double*)(slot + 32) = y;
            *(double*)(slot + 40) = z;
            *(float*)(slot + 48) = yaw;
            *(float*)(slot + 52) = pitch;
            *(float*)(slot + 56) = roll;
            *(float*)(slot + 60) = fov;
            *(float*)(slot + 64) = near;
            *(float*)(slot + 68) = far;
            *(int*)(slot + 72) = flags;
            *(float*)(slot + 76) = heat;
            *(float*)(slot + 80) = end;
            Thread.MemoryBarrier();
            *(long*)slot = seq + 2;
            _written++;
            *(long*)(_base + 16) = _written;
        }
    }
}
