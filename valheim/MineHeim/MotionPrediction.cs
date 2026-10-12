using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's picture of frame N is composited a frame or two after Valheim drew frame N, and by then the Viking has
    /// walked on: Steve (who stands where the Viking stood in frame N) trails behind him. The compositor's re-projection puts
    /// the static world right, but not something that moves with the camera. So Steve is sent to where the Viking will be
    /// when that picture is shown: his position plus his velocity times the measured delay (Link.PredictMotion).
    ///
    /// The delay is measured, not guessed: Minecraft's frame export (shared memory "Local\MineHeimFrame") says which of
    /// Valheim's frames its newest picture was rendered for. The same record gives the oracle: how far the Steve in the
    /// picture being shown stands from the Viking ("steve offset" in the perf line), with or without prediction.
    /// </summary>
    internal static class MotionPrediction
    {
        private const int History = 64;
        private const float MaxLead = 0.25f; // seconds: never extrapolate further than this (a slow PC can be 300 ms behind)

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

        [DllImport("kernel32.dll")]
        private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        private const uint FileMapRead = 0x0004;
        private static IntPtr _view;
        private static float _nextOpen;

        private static Vector3 _lastFeet, _velocity;
        private static bool _haveLast;
        private static float _lagFrames = 1.5f, _frameTime = 1f / 60f;
        private static readonly Vector3[] Sent = new Vector3[History], Actual = new Vector3[History];
        private static readonly long[] SentFrame = new long[History];
        private static double _errorSum;
        private static int _errorCount;

        /// <summary>Seconds ahead the Viking's position is being predicted (for the perf line).</summary>
        public static float Lead { get; private set; }

        /// <summary>
        /// Once per frame, before the pose goes out: the Viking's feet as Minecraft should draw them for `frame`.
        /// </summary>
        public static Vector3 Feet(long frame, Vector3 feet)
        {
            float dt = Time.deltaTime;
            if (dt > 1e-4f)
            {
                Vector3 v = _haveLast ? (feet - _lastFeet) / dt : Vector3.zero;
                // a teleport or a respawn is not a velocity
                _velocity = v.sqrMagnitude > 40f * 40f ? Vector3.zero : Vector3.Lerp(_velocity, v, 0.35f);
                _frameTime = Mathf.Lerp(_frameTime, Time.unscaledDeltaTime, 0.1f);
            }

            _lastFeet = feet;
            _haveLast = true;
            MeasureDelay(frame, feet);
            Lead = Plugin.PredictMotion.Value && dt > 1e-4f ? Mathf.Clamp(_lagFrames * _frameTime, 0f, MaxLead) : 0f;
            Vector3 predicted = feet + new Vector3(_velocity.x, 0f, _velocity.z) * Lead; // up and down: the ground decides
            int i = (int)(frame % History);
            Sent[i] = predicted;
            SentFrame[i] = frame;
            return predicted;
        }

        /// <summary>The average distance (metres) between the Viking and the Steve in the pictures shown since the last call.</summary>
        public static float TakeOffset()
        {
            float avg = _errorCount > 0 ? (float)(_errorSum / _errorCount) : -1f;
            _errorSum = 0;
            _errorCount = 0;
            return avg;
        }

        public static float LagFrames => _lagFrames;

        private static long _lastShown;
        private static int _mcPictures;

        /// <summary>Minecraft's new pictures since the last call (its frame rate over that time, as Valheim sees it).</summary>
        public static int TakeMinecraftPictures()
        {
            int n = _mcPictures;
            _mcPictures = 0;
            return n;
        }

        private static unsafe void MeasureDelay(long frame, Vector3 feet)
        {
            Actual[(int)(frame % History)] = feet;
            byte* p = (byte*)View();
            if (p == null || *(uint*)p != 0x5450434D || !Link.Connected)
            {
                return;
            }

            int slot = *(int*)(p + 40);
            int slots = *(int*)(p + 12);
            if (slot < 0 || slot >= slots || slots > 24)
            {
                return;
            }

            byte* desc = p + 256 + 128 * slot;
            long seq = *(long*)desc;
            long shown = *(long*)(desc + 16); // the Valheim frame Minecraft's newest picture was rendered for
            if ((seq & 1) != 0 || *(long*)desc != seq || shown <= 0 || shown > frame)
            {
                return;
            }

            // Valheim renders a frame behind its main thread: the picture composited next goes with frame - 1
            long lag = frame - 1 - shown;
            if (lag < 0 || lag >= History)
            {
                return;
            }

            _lagFrames = Mathf.Lerp(_lagFrames, lag, 0.05f);
            if (shown != _lastShown)
            {
                _lastShown = shown;
                _mcPictures++; // a new picture from Minecraft: its frame rate as it reaches Valheim
            }

            int i = (int)(shown % History);
            if (SentFrame[i] == shown)
            {
                _errorSum += Vector3.Distance(new Vector3(Sent[i].x, 0f, Sent[i].z), new Vector3(Actual[(int)((frame - 1) % History)].x, 0f, Actual[(int)((frame - 1) % History)].z));
                _errorCount++;
            }
        }

        private static IntPtr View()
        {
            if (_view != IntPtr.Zero || Time.unscaledTime < _nextOpen)
            {
                return _view;
            }

            _nextOpen = Time.unscaledTime + 2f;
            IntPtr mapping = OpenFileMappingW(FileMapRead, false, "Local\\MineHeimFrame");
            if (mapping != IntPtr.Zero)
            {
                // the header only (4096 bytes): the slot records live there. The handle stays open for the game's life, so a
                // restarted Minecraft finds the same mapping again.
                _view = MapViewOfFile(mapping, FileMapRead, 0, 0, new UIntPtr(4096));
            }

            return _view;
        }
    }
}
