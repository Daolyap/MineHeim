"""Alignment oracle: a Minecraft gold column is placed exactly where the fake host's red pillar is. For several
camera poses (orbit, varying yaw/pitch/FOV) the pillar's analytic silhouette (host_render) is compared with the
silhouette Minecraft actually drew (world alpha). If the camera conventions (position, yaw, pitch, FOV, aspect) agree,
the two coincide to about a pixel.

    python align_test.py [seconds] [outdir]

Needs a running Minecraft with the mineheim mod (gradlew runClient). Prints one line per frame and a verdict;
writes overlay PNGs (MC silhouette = green, host silhouette = red, overlap = yellow) to outdir.
"""
import asyncio
import json
import math
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image
import websockets

sys.path.insert(0, str(Path(__file__).parent))
import fakehost as fh  # noqa: E402
from mcframe import MCFrames  # noqa: E402

PILLAR = fh.HOST_PILLAR  # ((-3, 64, 5), (-2, 67, 6)) = block column x=-3, z=5, y=64..66


def masks(frame, pose):
    eye, yaw, pitch, fov = pose
    host_rgb, _ = fh.host_render(frame.width, frame.height, eye, yaw, pitch, fov, [(PILLAR, (1.0, 0.0, 0.0))])
    host = (host_rgb[..., 0] > 0.5) & (host_rgb[..., 1] < 0.2) & (host_rgb[..., 2] < 0.2)
    mc = frame.world_rgba[..., 3] > 127
    return host, mc


def bbox(m):
    ys, xs = np.nonzero(m)
    return (xs.min(), ys.min(), xs.max(), ys.max()) if len(xs) else None


async def run(seconds, outdir):
    out = Path(outdir)
    out.mkdir(parents=True, exist_ok=True)
    results = []
    async with websockets.connect(fh.URL, max_size=None) as ws:
        await ws.recv()
        cols = [v for x in range(-40, 41) for z in range(-40, 41) for v in (x, z, int(fh.GROUND_Y) - 1, int(fh.GROUND_Y) - 1)]
        await ws.send(json.dumps({"t": "clear"}))
        await ws.send(json.dumps({"t": "ground", "c": cols}))
        for c in ["fill -12 64 -12 12 80 12 minecraft:air", "fill -3 64 5 -3 66 5 minecraft:gold_block"]:
            await ws.send(json.dumps({"t": "cmd", "c": c}))
        await asyncio.sleep(1.0)

        async def drain():
            async for _ in ws:
                pass
        drain_task = asyncio.create_task(drain())
        poses = {}
        reader = None
        seen = set()
        t0 = time.perf_counter()
        f = 0
        while (now := time.perf_counter() - t0) < seconds and len(results) < 10:
            f += 1
            a = now * 0.6
            r = 5.0 + 3.0 * math.sin(now * 0.4)
            fov = 55.0 + 20.0 * math.sin(now * 0.3)
            eye = (-2.5 + r * math.sin(a), fh.GROUND_Y + 1.5 + math.sin(a * 0.7), 5.5 + r * math.cos(a))
            yaw, pitch = fh.look_at(eye, (-2.5, fh.GROUND_Y + 1.5, 5.5))
            poses[f] = (eye, yaw, pitch, fov)
            await ws.send(json.dumps({"t": "cam", "f": f, "p": list(eye), "r": [yaw, pitch, 0.0], "fov": fov, "fp": True}))
            if reader is None:
                try:
                    reader = MCFrames()
                except (OSError, RuntimeError):
                    pass
            if reader is not None and now > 2.5 and f % 20 == 0:
                fr = reader.latest()
                if fr is not None and fr.host_frame in poses and fr.mc_frame not in seen:
                    seen.add(fr.mc_frame)
                    host, mc = masks(fr, poses[fr.host_frame])
                    inter, union = np.logical_and(host, mc).sum(), np.logical_or(host, mc).sum()
                    if union < 200:  # pillar off screen for this pose
                        await asyncio.sleep(1 / 60)
                        continue
                    iou = inter / union
                    bh, bm = bbox(host), bbox(mc)
                    d = [int(bm[i] - bh[i]) for i in range(4)] if bh and bm else None
                    results.append((iou, d))
                    n = len(results)
                    print(f"frame {n}: host_frame {fr.host_frame} fov {poses[fr.host_frame][3]:.1f} IoU {iou:.4f}  "
                          f"host px {int(host.sum())} mc px {int(mc.sum())}  bbox delta (l,t,r,b) {d}")
                    img = np.zeros((fr.height, fr.width, 3), np.uint8)
                    img[..., 0] = host * 255
                    img[..., 1] = mc * 255
                    Image.fromarray(img).save(out / f"align_{n}.png")
            await asyncio.sleep(1 / 60)
        drain_task.cancel()
    if not results:
        print("NO RESULT: no frame matched a pose with the pillar in view")
        return 2
    ious = [r[0] for r in results]
    worst = max(max(abs(v) for v in r[1]) for r in results if r[1])
    print(f"\n{len(results)} frames: IoU min {min(ious):.4f} mean {np.mean(ious):.4f}; worst bbox edge offset {worst} px")
    print("PASS" if min(ious) > 0.93 and worst <= 3 else "FAIL")
    return 0 if min(ious) > 0.93 and worst <= 3 else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(run(float(sys.argv[1]) if len(sys.argv) > 1 else 25.0,
                             sys.argv[2] if len(sys.argv) > 2 else str(Path(__file__).resolve().parent.parent / "test-output" / "align_out"))))
