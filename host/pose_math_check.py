"""Offline check of a composite shot: with the host and Minecraft poses the add-on logged in the sidecar, project the
pillar cell myself (Minecraft's camera maths) and compare with the pillar the compositor actually drew.

    python pose_math_check.py <screenshot.png> [csv]
"""
import csv
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from fakehost import basis  # noqa: E402
from composite_oracle import DEFAULT_CSV, read_sidecar, load_rows  # noqa: E402


def project_box(pose, cell, w, h, height=3):
    x, y, z, yaw, pitch, roll, fov = pose
    fwd, right, up = basis(yaw, pitch)
    t = math.tan(math.radians(fov) / 2)
    pts = []
    for i in (0, 1):
        for j in (0, height):
            for k in (0, 1):
                v = np.array([cell[0] + i, cell[1] + j, cell[2] + k]) - np.array([x, y, z])
                d = float(v @ fwd)
                if d <= 0.05:
                    return None
                pts.append((float(v @ right) / (d * t * w / h), float(v @ up) / (d * t)))
    xs = [(p[0] * 0.5 + 0.5) * w for p in pts]
    ys = [(0.5 - p[1] * 0.5) * h for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def main(png, csv_path):
    side = read_sidecar(png)
    rows = load_rows(csv_path)
    host = side["host_pose"]
    mc = side["mc_pose"]
    r = rows.get(side["host_frame"])
    if len(sys.argv) > 5:
        cell = (int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]))
    else:
        cell = (int(r["cellx"]), int(r["celly"]), int(r["cellz"]))
    img = Image.open(png)
    w, h = img.size
    bh = project_box(host, cell, w, h)
    bm = project_box(mc, cell, w, h)
    print(f"pose delta (mc -> host): dx {host[0] - mc[0]:+.3f} dy {host[1] - mc[1]:+.3f} dz {host[2] - mc[2]:+.3f} m, "
          f"yaw {host[3] - mc[3]:+.3f} pitch {host[4] - mc[4]:+.3f} deg")
    if r is not None:
        csvbox = (float(r["x0"]), h - float(r["y1"]), float(r["x1"]), h - float(r["y0"]))
        print(f"CSV (Unity) box for host frame: x {csvbox[0]:.1f}..{csvbox[2]:.1f}")
    print(f"my projection, host pose:      x {bh[0]:.1f}..{bh[2]:.1f}   (centre {(bh[0] + bh[2]) / 2:.1f})")
    print(f"my projection, Minecraft pose: x {bm[0]:.1f}..{bm[2]:.1f}   (centre {(bm[0] + bm[2]) / 2:.1f})")
    print(f"warp_t {side['warp_t']}  mc_planes {side['mc_planes']}")
    # where the compositor actually drew the pillar (neutral look: gold)
    a = np.asarray(img.convert("RGB")).astype(int)
    r_, g_, b_ = a[..., 0], a[..., 1], a[..., 2]
    gold = (r_ > 120) & (g_ > 95) & (b_ < 0.55 * r_) & (r_ - b_ > 70) & (r_ >= g_ * 0.95) & (g_ >= r_ * 0.6)
    gate = np.zeros_like(gold)
    gate[:, max(0, int(bm[0]) - 60):int(bm[2]) + 60] = True
    gold &= gate
    cols = gold.sum(axis=0)
    ok = np.where(cols >= max(3, 0.2 * cols.max()))[0]
    print(f"drawn pillar columns: {ok.min()}..{ok.max() + 1} (centre {(ok.min() + ok.max() + 1) / 2:.1f})")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else DEFAULT_CSV)
