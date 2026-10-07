"""Wait for a moment when the pillar is clear of Steve, then save Valheim's screenshot next to the same frame with
Minecraft's world layer on top (premultiplied alpha, NO depth test: that is what the compositor does later, step 5).

    python snapshot_composite.py out.png [timeout_seconds] [um_exe]

Needs the plugin's F9 pillar test running (mineheim-proj.csv) and Valheim visible. Left: Valheim alone.
Right: Valheim + Minecraft (Steve and the gold pillar). The pillar should stand where Unity says it does.
"""
import csv
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from mcframe import MCFrames  # noqa: E402
from fakehost import basis  # noqa: E402

CSV = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\mineheim-proj.csv"


def clear_pillar_frame(fr, rows):
    row = rows.get(fr.host_frame)
    if row is None:
        return None
    w, h = int(row["width"]), int(row["height"])
    x0, y0, x1, y1 = (float(row[k]) for k in ("x0", "y0", "x1", "y1"))
    px0, px1, py0, py1 = x0, x1, h - y1, h - y0
    if px0 < 2 or py0 < 2 or px1 > w - 2 or py1 > h - 2:
        return None
    cx, cy, cz = fr.cam
    fwd, _, _ = basis(fr.rot[0], fr.rot[1])
    cell = (int(row["cellx"]), int(row["celly"]), int(row["cellz"]))
    d_near = min(float(np.dot(np.array([cell[0] + i, cell[1] + 3 * j, cell[2] + k]) - np.array([cx, cy, cz]), fwd))
                 for i in (0, 1) for j in (0, 1) for k in (0, 1))
    drawn = fr.world_rgba[..., 3] > 0
    nearer = drawn & (fr.world_depth_m < d_near - 0.25)
    if nearer[max(0, int(py0) - 2):int(py1) + 3, max(0, int(px0) - 2):int(px1) + 3].any():
        return None
    return (px0, py0, px1, py1)


def main(out, timeout, um):
    reader = MCFrames()
    t0 = time.perf_counter()
    last = 0
    rows = {}

    def load():
        out_rows = {}
        try:
            with open(CSV, newline="") as f:
                for r in csv.DictReader(f):
                    try:
                        out_rows[int(r["frame"])] = r
                    except (KeyError, ValueError, TypeError):
                        pass
        except OSError:
            pass
        return out_rows

    while time.perf_counter() - t0 < timeout:
        if time.perf_counter() - last > 1.0:
            rows = load()
            last = time.perf_counter()
        fr = reader.latest()
        box = clear_pillar_frame(fr, rows) if fr is not None else None
        if box is None:
            time.sleep(0.05)
            continue
        shot = Path(out).with_name(Path(out).stem + "_valheim.png")
        subprocess.run([um, "win", "shot", "--exe", "valheim.exe", str(shot)], check=True, capture_output=True)
        fr2 = reader.latest()
        rows = load()  # the screenshot took seconds: the frame we composite is newer than the rows read before it
        box = clear_pillar_frame(fr2, rows)  # the box for the frame we actually composite (the camera may have moved on)
        if box is None:
            continue
        v = np.asarray(Image.open(shot).convert("RGB")).astype(float) / 255.0
        w = fr2.world_rgba.astype(float) / 255.0
        comp = np.clip(w[..., :3] + v * (1 - w[..., 3:4]), 0, 1)
        # mark Unity's projected pillar box (thin red outline) on the composite
        img = (comp * 255).astype(np.uint8)
        xa, ya, xb, yb = (int(round(b)) for b in box)
        img[ya:yb + 1, [xa, xb]] = (255, 0, 0)
        img[[ya, yb], xa:xb + 1] = (255, 0, 0)
        side = np.concatenate([(v * 255).astype(np.uint8), img], axis=1)
        Image.fromarray(side).save(out)
        print(f"saved {out}: host_frame {fr.host_frame}, Unity's box {box[0]:.0f},{box[1]:.0f}-{box[2]:.0f},{box[3]:.0f} (red outline)")
        return 0
    print("no clear-pillar frame within the timeout")
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 60.0,
                  sys.argv[3] if len(sys.argv) > 3 else str(Path.home() / ".local" / "bin" / "um.exe")))
