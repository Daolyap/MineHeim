"""Watch Minecraft's exported frames for a few seconds and report which host frame numbers came back.

    python frames_check.py [seconds]

Used with a host that numbers its "cam" messages (the Valheim plugin, or Core.Tests `live`): if the host's frame
counter appears here as host_frame and keeps advancing, the host's camera messages are being parsed and used.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from mcframe import MCFrames  # noqa: E402


def main(seconds):
    reader = None
    seen = []
    first = None
    t0 = time.perf_counter()
    last_mc = -1
    while time.perf_counter() - t0 < seconds:
        if reader is None:
            try:
                reader = MCFrames()
            except (OSError, RuntimeError):
                time.sleep(0.2)
                continue
        fr = reader.latest(copy=False)
        if fr is not None and fr.mc_frame != last_mc:
            last_mc = fr.mc_frame
            if first is None:
                first = (fr.width, fr.height, fr.fov, fr.cam, fr.rot)
            seen.append(fr.host_frame)
        time.sleep(0.01)
    if not seen:
        print("NO FRAMES")
        return 2
    nonzero = [h for h in seen if h > 0]
    print(f"{len(seen)} Minecraft frames read; host_frame values: {len(nonzero)} non-zero, first {nonzero[:3]}, last {nonzero[-3:]}")
    print(f"size {first[0]}x{first[1]}, first fov {first[2]:.1f}, first camera {tuple(round(v, 2) for v in first[3])}")
    advancing = len(nonzero) >= 5 and nonzero[-1] > nonzero[0]
    print("PASS: host frame numbers come back and advance" if advancing else "FAIL: no advancing host_frame")
    return 0 if advancing else 1


if __name__ == "__main__":
    sys.exit(main(float(sys.argv[1]) if len(sys.argv) > 1 else 10.0))
