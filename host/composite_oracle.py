"""Oracle for the compositor (step 5): in ReShade's own screenshot, does Minecraft's gold pillar land where Unity says it
should for the pose that frame was composited with?

    python composite_oracle.py <screenshot.png> [csv]

Needs <screenshot.png>.txt (written by the MineHeim add-on next to every ReShade screenshot: which Valheim pose
frame and which Minecraft frame were used) and the plugin's mineheim-proj.csv (F9 pillar test). Prints the pixel
offsets between the pillar's silhouette in the composite and Unity's projected box, plus the latencies involved.

The composite relights Minecraft (night, haze), so the pillar is found by hue (yellow, red >= green >> blue), not by the
exact gold. Valheim's own geometry can legitimately hide part of the pillar (grass, a stone in front): the left, top and
right edges are the verdict, the bottom edge is reported only.
"""
import csv
import sys
from pathlib import Path

import numpy as np
from PIL import Image

DEFAULT_CSV = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\mineheim-proj.csv"
MARGIN = 45


def read_sidecar(png):
    out = {}
    for line in Path(str(png) + ".txt").read_text().splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            try:
                out[k.strip()] = int(v)
            except ValueError:
                out[k.strip()] = [float(x) for x in v.split()]  # the pose and warp diagnostics
    return out


def decode_codes(img):
    """The frame barcode the shader stamps in the top-left corner (DebugCode): two rows of 24 bits, 4 px per bit.
    Row 1: Valheim's pose frame, row 2: Minecraft's frame. None if the corner holds no barcode."""
    a = np.asarray(img.convert("L")).astype(int)
    out = []
    for row in (0, 1):
        y = row * 4 + 2
        value = 0
        for bit in range(24):
            cell = a[y - 1:y + 2, bit * 4 + 1:bit * 4 + 4]
            if cell.max() - cell.min() > 60:
                return None  # not a clean black/white cell
            value |= (1 if cell.mean() > 128 else 0) << bit
        out.append(value)
    return out


def load_rows(csv_path):
    rows = {}
    with open(csv_path, newline="") as f:
        for r in csv.DictReader(f):
            try:
                rows[int(r["frame"])] = r
            except (KeyError, ValueError, TypeError):
                pass
    return rows


def main(png, csv_path):
    side = read_sidecar(png)
    codes = decode_codes(Image.open(png).convert("RGB"))
    if codes is not None and codes[0] > 0:
        # the picture says which frames it was composited from: trust it over the (asynchronously written) sidecar
        if codes[0] != side["host_frame"] or codes[1] != side["mc_frame"]:
            print(f"barcode in the picture: host {codes[0]}, minecraft {codes[1]} (the sidecar said {side['host_frame']}/{side['mc_frame']}: it was written later)")
        side["host_frame"], side["mc_frame"] = codes
        side["newest_pose_frame"] = max(side["newest_pose_frame"], codes[0])
    else:
        print("no barcode in the picture: using the sidecar's frame numbers (they may be a few frames late)")
    frame = side["host_frame"]
    rows = load_rows(csv_path)
    row = rows.get(frame)
    mc_row = rows.get(side["mc_frame"])
    print("frames:", {k: side[k] for k in ("host_frame", "mc_frame")})
    print(f"Minecraft frame is {side['newest_pose_frame'] - side['mc_frame']} frames behind the newest pose; "
          f"the composite used pose {frame} ({side['newest_pose_frame'] - frame} behind the newest)")
    if row is None:
        print("NO ROW: the pillar was not in front of the camera for that pose (no CSV row)")
        return 2
    img = np.asarray(Image.open(png).convert("RGB")).astype(int)
    h, w, _ = img.shape
    x0, y0, x1, y1 = (float(row[k]) for k in ("x0", "y0", "x1", "y1"))
    px0, px1, py0, py1 = x0, x1, h - y1, h - y0
    if px0 < 2 or py0 < 2 or px1 > w - 2 or py1 > h - 2:
        print("pillar is partly off screen for that pose: no clean box")
        return 2
    r_, g_, b_ = img[..., 0], img[..., 1], img[..., 2]
    # with compositor/neutral_look.py on, Minecraft's colours arrive unchanged: gold = (246,208,61) lit, darker on the sides
    yellow = (r_ > 120) & (g_ > 95) & (b_ < 0.55 * r_) & (r_ - b_ > 70) & (r_ >= g_ * 0.95) & (g_ >= r_ * 0.6)
    gate = np.zeros_like(yellow)
    gate[max(0, int(py0 - MARGIN)):int(py1 + MARGIN), max(0, int(px0 - MARGIN)):int(px1 + MARGIN)] = True
    mask = yellow & gate
    if mask.sum() < 80:
        print(f"pillar not found in the screenshot (expected box {px0:.0f},{py0:.0f}-{px1:.0f},{py1:.0f}, {int(mask.sum())} yellow px)")
        return 2
    # keep the dominant column band: strip stray yellow-green pixels (grass) far from the pillar's own columns
    cols = mask.sum(axis=0)
    ok = np.where(cols >= max(3, 0.2 * cols.max()))[0]
    mx0, mx1 = ok.min(), ok.max() + 1
    mask[:, :mx0] = False
    mask[:, mx1:] = False
    ys = np.where(mask.sum(axis=1) >= 2)[0]
    my0, my1 = ys.min(), ys.max() + 1
    d = (mx0 - px0, my0 - py0, mx1 - px1, my1 - py1)
    print(f"expected box (Unity) {px0:.0f},{py0:.0f}-{px1:.0f},{py1:.0f}; measured {mx0},{my0}-{mx1},{my1}")
    print(f"delta px (left, top, right, bottom) = ({d[0]:+.1f}, {d[1]:+.1f}, {d[2]:+.1f}, {d[3]:+.1f})   [bottom may be hidden by Valheim's geometry]")
    cx_meas = (mx0 + mx1) / 2.0
    cx_host = (px0 + px1) / 2.0
    line = f"centre x: measured {cx_meas:.1f}, where Unity puts it for the presented pose {cx_host:.1f} (error {cx_meas - cx_host:+.1f} px)"
    if mc_row is not None:
        cx_mc = (float(mc_row["x0"]) + float(mc_row["x1"])) / 2.0
        line += f"; without re-projection it would sit at {cx_mc:.1f} (lag shift {cx_mc - cx_host:+.1f} px)"
        print("RESULT", f"{cx_meas - cx_host:+.2f}", f"{cx_mc - cx_host:+.2f}")
    print(line)
    worst = max(abs(d[0]), abs(d[1]), abs(d[2]))
    print("PASS (left/top/right within 4 px)" if worst <= 4.0 else f"FAIL (worst of left/top/right {worst:.1f} px)")
    return 0 if worst <= 4.0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else DEFAULT_CSV))
