"""Step 4 oracle with the real Valheim: does Minecraft's gold pillar land where Unity's own camera says it should?

The plugin (press F9 in Valheim) places a gold pillar on a known Minecraft block cell and, for every camera pose it sends
to Minecraft, writes Unity's own projection of that pillar (Camera.WorldToScreenPoint of the 8 corners) to
BepInEx/mineheim-proj.csv, keyed by the frame number it put in the "cam" message. Minecraft echoes that number in
the frame it exports, so each exported frame can be compared with the projection of the pose it was rendered with.

    python pose_oracle.py [seconds] [outdir] [csv]

Prints one line per compared frame: offsets in pixels between the pillar's silhouette box in Minecraft's frame and the
projected box (left, top, right, bottom; positive = Minecraft's edge is further right/down).
"""
import csv
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from mcframe import MCFrames  # noqa: E402
from fakehost import basis  # noqa: E402  (Minecraft camera axes for a yaw/pitch)

DEFAULT_CSV = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\mineheim-proj.csv"
MARGIN = 60  # only look for the pillar this close around where Unity says it is


def read_rows(path):
    rows = {}
    try:
        with open(path, newline="") as f:
            for r in csv.DictReader(f):
                try:
                    rows[int(r["frame"])] = r
                except (KeyError, ValueError, TypeError):
                    pass  # a line still being written
    except OSError:
        pass
    return rows


def gold_mask(frame):
    rgba = frame.world_rgba.astype(np.int32)  # premultiplied; the pillar is opaque so this is its colour
    r, g, b, a = rgba[..., 0], rgba[..., 1], rgba[..., 2], rgba[..., 3]
    return (a > 200) & (r > 150) & (g > 110) & (b < 0.55 * r) & (r - b > 70)


def main(seconds, outdir, csv_path):
    out = Path(outdir)
    out.mkdir(parents=True, exist_ok=True)
    reader = None
    rows = {}
    results = []
    seen = set()
    kept_poses = set()
    occluded = 0
    stats = {'frames': 0, 'no_row': 0, 'offscreen': 0, 'duplicate_pose': 0, 'not_found': 0}
    t0 = time.perf_counter()
    last_reload = 0.0
    while time.perf_counter() - t0 < seconds:
        if reader is None:
            try:
                reader = MCFrames()
            except (OSError, RuntimeError):
                time.sleep(0.3)
                continue
        now = time.perf_counter()
        if now - last_reload > 1.0:
            rows = read_rows(csv_path)
            last_reload = now
        fr = reader.latest()
        if fr is None or fr.mc_frame in seen:
            time.sleep(0.01)
            continue
        seen.add(fr.mc_frame)
        stats['frames'] += 1
        if fr.host_frame not in rows:
            stats['no_row'] += 1  # the pillar was not in front of Valheim's camera for that pose (no row written)
            continue
        row = rows[fr.host_frame]
        w, h = int(row["width"]), int(row["height"])
        if (fr.width, fr.height) != (w, h):
            print(f"frame {fr.host_frame}: size mismatch, Minecraft {fr.width}x{fr.height} vs Valheim {w}x{h}")
            continue
        x0, y0, x1, y1 = (float(row[k]) for k in ("x0", "y0", "x1", "y1"))
        # Unity's screen origin is bottom-left; image rows run top-down
        px0, px1, py0, py1 = x0, x1, h - y1, h - y0
        if px0 < 2 or py0 < 2 or px1 > w - 2 or py1 > h - 2:
            stats['offscreen'] += 1
            continue  # partly off screen: no clean box
        # depth-based masking: the pillar's nearest point along the view axis, from the pose Minecraft rendered with.
        # Anything Minecraft drew nearer than that (Steve) hides part of the pillar; the pillar itself is the rest.
        cx, cy, cz = fr.cam
        fwd, _, _ = basis(fr.rot[0], fr.rot[1])
        cell = (int(row["cellx"]), int(row["celly"]), int(row["cellz"]))
        d_near = min(
            float(np.dot(np.array([cell[0] + i, cell[1] + 3 * j, cell[2] + k]) - np.array([cx, cy, cz]), fwd))
            for i in (0, 1) for j in (0, 1) for k in (0, 1)
        )
        depth = fr.world_depth_m
        drawn = fr.world_rgba[..., 3] > 0
        nearer = drawn & (depth < d_near - 0.25)
        pad = 2
        if nearer[max(0, int(py0) - pad):int(py1) + pad + 1, max(0, int(px0) - pad):int(px1) + pad + 1].any():
            occluded += 1
            continue
        mask = drawn & (depth >= d_near - 0.25)
        key = (round(px0 / 3), round(py0 / 3), round(px1 / 3), round(py1 / 3))
        if key in kept_poses:
            stats['duplicate_pose'] += 1
            continue  # a pose already measured: want variety, not 25 copies of one frame
        kept_poses.add(key)
        gate = np.zeros_like(mask)
        gate[max(0, int(py0 - MARGIN)):int(py1 + MARGIN), max(0, int(px0 - MARGIN)):int(px1 + MARGIN)] = True
        mask &= gate
        if mask.sum() < 50:
            stats['not_found'] += 1
            print(f"frame {fr.host_frame}: pillar not found in Minecraft's frame (expected box {px0:.0f},{py0:.0f} - {px1:.0f},{py1:.0f})")
            continue
        ys, xs = np.nonzero(mask)
        mx0, mx1, my0, my1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        d = (mx0 - px0, my0 - py0, mx1 - px1, my1 - py1)
        results.append(d)
        n = len(results)
        print(f"frame {fr.host_frame}: delta px (l,t,r,b) = ({d[0]:+.1f}, {d[1]:+.1f}, {d[2]:+.1f}, {d[3]:+.1f})   "
              f"expected box {px0:.0f},{py0:.0f}-{px1:.0f},{py1:.0f}  box {w}x{h}")
        if n <= 6:
            img = np.array(fr.world_rgba[..., :3], dtype=np.uint8).copy()
            for (xa, ya, xb, yb, col) in ((px0, py0, px1, py1, (255, 0, 0)), (mx0, my0, mx1, my1, (0, 255, 0))):
                xa, ya, xb, yb = int(max(0, xa)), int(max(0, ya)), int(min(w - 1, xb)), int(min(h - 1, yb))
                img[ya:yb + 1, [xa, xb]] = col
                img[[ya, yb], xa:xb + 1] = col
            Image.fromarray(img).save(out / f"pose_{n}.png")
        if n >= 25:
            break
    print("frame accounting:", stats, "occluded:", occluded)
    if not results:
        print(f"NO RESULT: no exported frame matched a logged pose with the pillar fully in view and unoccluded ({occluded} occluded frames skipped)")
        return 2
    a = np.array(results)
    worst = np.abs(a).max()
    print(f"\n{len(a)} frames: mean |delta| {np.abs(a).mean():.2f} px, worst {worst:.1f} px; "
          f"mean signed (l,t,r,b) = {tuple(round(v, 2) for v in a.mean(axis=0))}")
    print(f"{occluded} frames skipped because Minecraft drew something in front of the pillar; {len(kept_poses)} distinct poses")
    verdict = worst <= 3.0
    print("PASS (within 3 px)" if verdict else "FAIL")
    return 0 if verdict else 1


if __name__ == "__main__":
    sys.exit(main(float(sys.argv[1]) if len(sys.argv) > 1 else 30.0,
                  sys.argv[2] if len(sys.argv) > 2 else str(Path(__file__).resolve().parent.parent / "test-output" / "pose_out"),
                  sys.argv[3] if len(sys.argv) > 3 else DEFAULT_CSV))
