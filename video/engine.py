"""A small motion-graphics engine for MineHeim's video segments: anti-aliased sprites (drawn at 2x, downscaled once), eased
over time, composited per frame and piped into ffmpeg at 2560x1440, 60 fps. No dependencies beyond Pillow and ffmpeg."""
from __future__ import annotations

import math
import os
import subprocess
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H, FPS = 2560, 1440, 60
SS = 2  # supersampling for sprites
FONTS = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"
ROOT = Path(__file__).resolve().parent.parent
MEDIA = ROOT / "docs" / "media"
OUT = Path(__file__).resolve().parent / "out"

# palette (matches video/brand.py)
NIGHT = (12, 15, 21)
INK = (238, 233, 222)
MUTED = (170, 162, 146)
GRASS = (98, 168, 58)
DIRT = (134, 96, 67)
BRONZE = (176, 120, 52)
GOLD = (236, 190, 92)
STONE = (88, 94, 102)
SKY = (82, 140, 196)
RED = (214, 82, 64)
PANEL = (24, 29, 38)
LINE = (70, 78, 92)


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    for candidate in (name, "segoeuib.ttf", "arialbd.ttf"):
        p = FONTS / candidate
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def head(size):  # headings
    return font("bahnschrift.ttf", size)


def body(size):
    return font("segoeui.ttf", size)


def bold(size):
    return font("segoeuib.ttf", size)


def mono(size):
    return font("consola.ttf", size)


# ------------------------------------------------------------------ easing

def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def ease_out(t):
    t = clamp(t)
    return 1 - (1 - t) ** 3


def ease_in_out(t):
    t = clamp(t)
    return 4 * t ** 3 if t < 0.5 else 1 - (-2 * t + 2) ** 3 / 2


def back_out(t):
    t = clamp(t)
    c = 1.7
    return 1 + (c + 1) * (t - 1) ** 3 + c * (t - 1) ** 2


def window(t, start, dur):
    """0 before start, ramps to 1 over dur."""
    return clamp((t - start) / dur) if dur > 0 else float(t >= start)


# ------------------------------------------------------------------ sprites

def sprite(w: int, h: int, draw: Callable[[ImageDraw.ImageDraw, Image.Image, int], None]) -> Image.Image:
    """Draw at SS x size with a callback(draw, image, scale), then downscale: anti-aliased RGBA."""
    big = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    draw(ImageDraw.Draw(big), big, SS)
    return big.resize((w, h), Image.LANCZOS)


def text_sprite(text: str, f: ImageFont.FreeTypeFont, fill=INK, shadow=True, max_width: int | None = None, line_gap=1.25,
                align="left") -> Image.Image:
    """Multi-line text (wrapped to max_width) as an RGBA sprite, with a soft shadow."""
    fs = font_path_scaled(f, SS)
    lines = wrap(text, fs, max_width * SS if max_width else None)
    asc, desc = fs.getmetrics()
    lh = int((asc + desc) * line_gap)
    tw = max(int(ImageDraw.Draw(Image.new("L", (1, 1))).textlength(l, font=fs)) for l in lines) if lines else 1
    pad = 12 * SS
    big = Image.new("RGBA", (tw + pad * 2, lh * len(lines) + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    for i, l in enumerate(lines):
        lw = d.textlength(l, font=fs)
        x = pad + (tw - lw) / 2 if align == "center" else pad
        d.text((x, pad + i * lh), l, font=fs, fill=fill)
    out = big.resize((big.width // SS, big.height // SS), Image.LANCZOS)
    if shadow:
        sh = Image.new("RGBA", out.size, (0, 0, 0, 0))
        sh.putalpha(out.getchannel("A").point(lambda a: int(a * 0.6)))
        sh = sh.filter(ImageFilter.GaussianBlur(6))
        o = Image.new("RGBA", out.size, (0, 0, 0, 0))
        o.alpha_composite(sh, (0, 0))
        o.alpha_composite(out, (0, 0))
        return o
    return out


_scaled_cache: dict = {}


def font_path_scaled(f: ImageFont.FreeTypeFont, s: int) -> ImageFont.FreeTypeFont:
    key = (f.path, f.size, s)
    if key not in _scaled_cache:
        _scaled_cache[key] = ImageFont.truetype(f.path, f.size * s)
    return _scaled_cache[key]


def wrap(text: str, f, max_w):
    out = []
    for para in text.split("\n"):
        if max_w is None:
            out.append(para)
            continue
        words, line = para.split(" "), ""
        d = ImageDraw.Draw(Image.new("L", (1, 1)))
        for w_ in words:
            trial = (line + " " + w_).strip()
            if d.textlength(trial, font=f) <= max_w or not line:
                line = trial
            else:
                out.append(line)
                line = w_
        out.append(line)
    return out


def rounded_box(w, h, fill=PANEL, outline=LINE, radius=24, width=3, accent=None) -> Image.Image:
    def draw(d, img, s):
        d.rounded_rectangle([0, 0, w * s - 1, h * s - 1], radius=radius * s, fill=fill + (235,), outline=outline, width=width * s)
        if accent:
            d.rounded_rectangle([0, 0, 10 * s, h * s - 1], radius=radius * s, fill=accent)
            d.rectangle([5 * s, 0, 10 * s, h * s - 1], fill=accent)
    return sprite(w, h, draw)


def labelled_box(w, h, title, subtitle="", accent=GOLD, title_size=46, sub_size=30, fill=PANEL) -> Image.Image:
    box = rounded_box(w, h, fill=fill, accent=accent)
    t = text_sprite(title, head(title_size), INK, shadow=False, max_width=w - 70)
    box.alpha_composite(t, (30, 18 if subtitle else (h - t.height) // 2))
    if subtitle:
        st = text_sprite(subtitle, body(sub_size), MUTED, shadow=False, max_width=w - 70)
        box.alpha_composite(st, (30, 18 + t.height - 14))
    return box


def arrow_sprite(x0, y0, x1, y1, color=GOLD, width=8, head_len=34, dashed=False) -> tuple[Image.Image, tuple[int, int]]:
    """An arrow from (x0,y0) to (x1,y1) in frame coordinates; returns the sprite and its top-left."""
    pad = head_len + width * 2
    lx, ly = min(x0, x1) - pad, min(y0, y1) - pad
    w, h = int(abs(x1 - x0) + pad * 2), int(abs(y1 - y0) + pad * 2)

    def draw(d, img, s):
        ax, ay, bx, by = (x0 - lx) * s, (y0 - ly) * s, (x1 - lx) * s, (y1 - ly) * s
        ang = math.atan2(by - ay, bx - ax)
        ex, ey = bx - math.cos(ang) * head_len * s * 0.8, by - math.sin(ang) * head_len * s * 0.8
        if dashed:
            n = max(1, int(math.hypot(ex - ax, ey - ay) / (28 * s)))
            for k in range(0, n, 2):
                p0 = (ax + (ex - ax) * k / n, ay + (ey - ay) * k / n)
                p1 = (ax + (ex - ax) * min(n, k + 1) / n, ay + (ey - ay) * min(n, k + 1) / n)
                d.line([p0, p1], fill=color, width=width * s)
        else:
            d.line([(ax, ay), (ex, ey)], fill=color, width=width * s)
        hl = head_len * s
        p1 = (bx, by)
        p2 = (bx - hl * math.cos(ang - 0.42), by - hl * math.sin(ang - 0.42))
        p3 = (bx - hl * math.cos(ang + 0.42), by - hl * math.sin(ang + 0.42))
        d.polygon([p1, p2, p3], fill=color)
    return sprite(w, h, draw), (int(lx), int(ly))


def load(name: str, width: int | None = None, radius=18, border=GOLD) -> Image.Image:
    """A screenshot from docs/media as a framed card (never upscaled beyond its own size x 1.5)."""
    im = Image.open(MEDIA / name).convert("RGBA")
    if width:
        width = min(width, int(im.width * 1.5))
        im = im.resize((width, int(im.height * width / im.width)), Image.LANCZOS)
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width - 1, im.height - 1], radius=radius, fill=255)
    framed = Image.new("RGBA", (im.width + 8, im.height + 8), (0, 0, 0, 0))
    ImageDraw.Draw(framed).rounded_rectangle([0, 0, framed.width - 1, framed.height - 1], radius=radius + 4, fill=border + (255,))
    framed.paste(im, (4, 4), mask)
    return framed


def fade(img: Image.Image, a: float) -> Image.Image:
    if a >= 0.999:
        return img
    out = img.copy()
    out.putalpha(img.getchannel("A").point(lambda v: int(v * clamp(a))))
    return out


def scaled(img: Image.Image, s: float) -> Image.Image:
    if abs(s - 1) < 0.01:
        return img
    return img.resize((max(1, int(img.width * s)), max(1, int(img.height * s))), Image.BILINEAR)


def place(frame: Image.Image, img: Image.Image, cx: float, cy: float, a: float = 1.0, s: float = 1.0):
    """Composite img centred at (cx, cy) with opacity a and scale s."""
    if a <= 0.01:
        return
    im = fade(scaled(img, s), a)
    frame.alpha_composite(im, (int(cx - im.width / 2), int(cy - im.height / 2)))


def place_tl(frame: Image.Image, img: Image.Image, x: float, y: float, a: float = 1.0):
    if a <= 0.01:
        return
    frame.alpha_composite(fade(img, a), (int(x), int(y)))


# ------------------------------------------------------------------ backgrounds

_bg_cache: dict = {}


def backdrop(kind: str = "night") -> Image.Image:
    """Static backgrounds: a dark gradient with a faint block grid ('night'), or a blurred screenshot ('shot:<file>')."""
    if kind in _bg_cache:
        return _bg_cache[kind]
    if kind.startswith("shot:"):
        im = Image.open(MEDIA / kind[5:]).convert("RGB")
        s = max(W / im.width, H / im.height)
        im = im.resize((int(im.width * s) + 2, int(im.height * s) + 2), Image.LANCZOS).crop((0, 0, W, H))
        im = Image.blend(im.filter(ImageFilter.GaussianBlur(10)), Image.new("RGB", (W, H), NIGHT), 0.6)
        bg = im.convert("RGBA")
    else:
        bg = Image.new("RGBA", (W, H), NIGHT + (255,))
        d = ImageDraw.Draw(bg)
        for y in range(H):
            k = y / H
            d.line([(0, y), (W, y)], fill=(int(12 + 10 * k), int(15 + 8 * k), int(21 + 12 * k), 255))
        g = 80
        for x in range(0, W, g):
            d.line([(x, 0), (x, H)], fill=(255, 255, 255, 9))
        for y in range(0, H, g):
            d.line([(0, y), (W, y)], fill=(255, 255, 255, 9))
    vign = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(vign)
    for i in range(30):
        vd.rectangle([i * W // 60, i * H // 60, W - i * W // 60, H - i * H // 60], fill=int(255 * i / 30))
    vign = vign.filter(ImageFilter.GaussianBlur(220))
    dark = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    bg = Image.composite(bg, dark, vign)
    _bg_cache[kind] = bg
    return bg


# ------------------------------------------------------------------ scenes and rendering

@dataclass
class Scene:
    """A full-frame segment: draw(frame, t) for t in [0, duration)."""
    name: str
    duration: float
    draw: Callable[[Image.Image, float], None]
    background: str = "night"
    alpha: bool = False  # True: transparent overlay (ProRes 4444 .mov), no background


def encode_cmd(path: Path, alpha: bool) -> list[str]:
    base = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba" if alpha else "rgb24",
            "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-"]
    if alpha:
        return base + ["-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le", "-vendor", "apl0", str(path)]
    return base + ["-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(path)]


def render(scene: Scene, path: Path, preview_every: float | None = None):
    path.parent.mkdir(parents=True, exist_ok=True)
    proc = subprocess.Popen(encode_cmd(path, scene.alpha), stdin=subprocess.PIPE)
    n = int(round(scene.duration * FPS))
    bg = None if scene.alpha else backdrop(scene.background)
    for i in range(n):
        t = i / FPS
        frame = Image.new("RGBA", (W, H), (0, 0, 0, 0)) if scene.alpha else bg.copy()
        scene.draw(frame, t)
        proc.stdin.write(frame.tobytes() if scene.alpha else frame.convert("RGB").tobytes())
        if preview_every and i % int(preview_every * FPS) == 0:
            frame.convert("RGB").resize((W // 4, H // 4)).save(path.with_suffix(f".{i:05d}.jpg"))
    proc.stdin.close()
    if proc.wait() != 0:
        raise RuntimeError("ffmpeg failed for " + str(path))


def still(scene: Scene, t: float) -> Image.Image:
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0)) if scene.alpha else backdrop(scene.background).copy()
    scene.draw(frame, t)
    return frame
