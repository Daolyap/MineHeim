"""Print the Minecraft block cell under the crosshair, `distance` blocks past the player along the camera's ray (so it is
within Steve's reach), from the camera Minecraft really rendered with and the player's position in the plugin's player log.

    python crosshair_cell.py <mineheim-player.csv> [distance=3]      ->  "x y z"

Needs Minecraft running with a host attached (its shared-memory frames carry the camera) and `playerlog on`.
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from fakehost import basis  # noqa: E402
from mcframe import MCFrames  # noqa: E402


def main(csv_path, distance):
    fr = MCFrames().latest()
    if fr is None:
        raise SystemExit("no Minecraft frame yet")
    last = Path(csv_path).read_text().strip().splitlines()[-1].split(",")
    px, py, pz = float(last[4]), float(last[5]) + 1.62, float(last[6])  # the player's eyes, Minecraft coordinates
    cx, cy, cz = fr.cam
    fwd, _, _ = basis(fr.rot[0], fr.rot[1])
    along = (px - cx) * fwd[0] + (py - cy) * fwd[1] + (pz - cz) * fwd[2]  # how far the eyes are along the camera ray
    t = along + distance
    x, y, z = cx + fwd[0] * t, cy + fwd[1] * t, cz + fwd[2] * t
    print(f"{math.floor(x)} {math.floor(y)} {math.floor(z)}")


if __name__ == "__main__":
    main(sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 3.0)
