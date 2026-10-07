"""MineHeim brand assets, drawn from scratch (no game logos or textures): the wordmark, the icon, the GitHub social preview
and the README banner. python video/brand.py  ->  docs/media/*.png"""
from __future__ import annotations

import math
import os
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
MEDIA = ROOT / "docs" / "media"
FONTS = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"

# palette: Minecraft's grass and dirt, Valheim's bronze, gold and night
GRASS = (98, 168, 58)
GRASS_DARK = (64, 122, 36)
DIRT = (134, 96, 67)
DIRT_DARK = (96, 66, 44)
BRONZE = (176, 120, 52)
GOLD = (236, 190, 92)
GOLD_LIGHT = (255, 226, 150)
NIGHT = (14, 18, 24)
STONE = (88, 94, 102)
STONE_DARK = (58, 62, 70)

# a 5x7 pixel alphabet for the wordmark (our own glyphs)
GLYPHS = {
    "M": ["X...X", "XX.XX", "X.X.X", "X.X.X", "X...X", "X...X", "X...X"],
    "I": ["XXXXX", "..X..", "..X..", "..X..", "..X..", "..X..", "XXXXX"],
    "N": ["X...X", "XX..X", "X.X.X", "X.X.X", "X..XX", "X...X", "X...X"],
    "E": ["XXXXX", "X....", "X....", "XXXX.", "X....", "X....", "XXXXX"],
    "H": ["X...X", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
}


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    for candidate in (name, "segoeuib.ttf", "arialbd.ttf"):
        path = FONTS / candidate
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def shade(c, f):
    return tuple(max(0, min(255, int(v * f))) for v in c)


def block_cell(d: ImageDraw.ImageDraw, x: int, y: int, s: int, top, side, rng: random.Random, grass_top: bool):
    """One pixel of the wordmark as a small bevelled block with a speckled texture."""
    d.rectangle([x, y, x + s - 1, y + s - 1], fill=side)
    n = max(2, s // 6)
    for _ in range(6):
        px, py = rng.randrange(0, s - n), rng.randrange(0, s - n)
        d.rectangle([x + px, y + py, x + px + n - 1, y + py + n - 1], fill=shade(side, rng.uniform(0.8, 1.15)))
    if grass_top:
        d.rectangle([x, y, x + s - 1, y + max(2, s // 3)], fill=top)
        for k in range(0, s, n * 2):
            d.rectangle([x + k, y + max(2, s // 3), x + k + n - 1, y + max(2, s // 3) + n], fill=top)
    b = max(1, s // 10)
    d.rectangle([x, y, x + s - 1, y + b - 1], fill=shade(top if grass_top else side, 1.25))
    d.rectangle([x, y, x + b - 1, y + s - 1], fill=shade(side, 1.15))
    d.rectangle([x, y + s - b, x + s - 1, y + s - 1], fill=shade(side, 0.6))
    d.rectangle([x + s - b, y, x + s - 1, y + s - 1], fill=shade(side, 0.7))


def wordmark(cell: int = 30, gap: int = 0, letters: str = "MINEHEIM", mine_count: int = 4) -> Image.Image:
    """MINE in grass-and-dirt blocks, HEIM in bronze-and-gold blocks, with a soft shadow. Transparent background."""
    rng = random.Random(7)
    letter_w = 5 * cell + 4 * gap
    spacing = int(cell * 0.9)
    word_gap = int(cell * 1.4)
    width = len(letters) * letter_w + (len(letters) - 1) * spacing + word_gap + cell * 2
    height = 7 * cell + 6 * gap + cell * 2
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x0 = cell
    for i, ch in enumerate(letters):
        mine = i < mine_count
        if i == mine_count and 0 < mine_count < len(letters):
            x0 += word_gap
        for row, line in enumerate(GLYPHS[ch]):
            for col, on in enumerate(line):
                if on != "X":
                    continue
                x = x0 + col * (cell + gap)
                y = cell + row * (cell + gap)
                if mine:
                    top_row = row == 0 or GLYPHS[ch][row - 1][col] != "X"
                    block_cell(d, x, y, cell, GRASS, DIRT, rng, top_row)
                else:
                    t = row / 6
                    side = tuple(int(BRONZE[k] * (1 - t) + shade(BRONZE, 0.7)[k] * t) for k in range(3))
                    block_cell(d, x, y, cell, GOLD, side, rng, row == 0 or GLYPHS[ch][row - 1][col] != "X")
        x0 += letter_w + spacing
    rim = Image.new("RGBA", img.size, (20, 14, 8, 0))
    rim.putalpha(img.getchannel("A").filter(ImageFilter.MaxFilter(max(3, (cell // 5) * 2 + 1))))
    rim.alpha_composite(img)
    img = rim
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    shadow.putalpha(img.getchannel("A").point(lambda a: int(a * 0.55)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(cell // 3))
    out = Image.new("RGBA", (width + cell, height + cell), (0, 0, 0, 0))
    out.alpha_composite(shadow, (cell // 2, cell // 2 + cell // 4))
    out.alpha_composite(img, (0, 0))
    return out


def icon(size: int = 512) -> Image.Image:
    """An isometric block: grass on top, Minecraft dirt on the left face, Norse rune stone with a gold rune on the right."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = size
    cx, top, edge = s / 2, s * 0.08, s * 0.42
    a = (cx, top)
    b = (cx + edge * math.cos(math.radians(30)), top + edge * 0.5)
    c = (cx, top + edge)
    e = (cx - edge * math.cos(math.radians(30)), top + edge * 0.5)
    h = s * 0.48
    left = [e, c, (c[0], c[1] + h), (e[0], e[1] + h)]
    right = [c, b, (b[0], b[1] + h), (c[0], c[1] + h)]
    d.polygon(left, fill=DIRT)
    d.polygon(right, fill=STONE)
    d.polygon([a, b, c, e], fill=GRASS)
    rng = random.Random(3)
    base = img.copy()
    d = ImageDraw.Draw(img)
    # texture: square speckles on the dirt, mossy grass fringe, chisel marks on the stone (clipped to the faces below)
    for _ in range(140):
        u, v = rng.random(), rng.random()
        x = e[0] + (c[0] - e[0]) * u
        y = e[1] + (c[1] - e[1]) * u + h * v
        q = s * 0.018
        d.polygon([(x, y), (x + q, y + q * 0.5), (x + q, y + q * 1.5), (x, y + q)], fill=shade(DIRT, rng.uniform(0.7, 1.2)))
    for k in range(9):
        u = (k + 0.5) / 9
        x = e[0] + (c[0] - e[0]) * u
        y = e[1] + (c[1] - e[1]) * u
        d.polygon([(x - s * 0.02, y), (x + s * 0.02, y + s * 0.02), (x + s * 0.02, y + s * 0.06 + rng.random() * s * 0.03), (x - s * 0.02, y + s * 0.04)], fill=GRASS_DARK)
    for _ in range(60):
        u, v = rng.random(), rng.random()
        x = c[0] + (b[0] - c[0]) * u
        y = c[1] + (b[1] - c[1]) * u + h * v
        d.line([(x, y), (x + s * 0.03, y - s * 0.015)], fill=shade(STONE, rng.uniform(0.75, 1.2)), width=max(1, s // 170))
    for _ in range(80):
        u, v = rng.random(), rng.random()
        x = e[0] + (b[0] - e[0]) * u + (a[0] - cx) * 0
        y = a[1] + (c[1] - a[1]) * v
        if d.im.getpixel((int(x), int(y)))[:3] == GRASS:
            d.rectangle([x, y, x + s * 0.015, y + s * 0.015], fill=shade(GRASS, rng.uniform(0.8, 1.2)))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(left, fill=255)
    ImageDraw.Draw(mask).polygon(right, fill=255)
    ImageDraw.Draw(mask).polygon([a, b, c, e], fill=255)
    img = Image.composite(img, base, mask)
    clean = Image.new("RGBA", img.size, (0, 0, 0, 0))
    clean.paste(img, (0, 0), mask)
    img = clean
    d = ImageDraw.Draw(img)
    # the rune Mannaz (M) carved in gold on the stone face, drawn in the face's skewed plane
    def face(u, v):
        return (c[0] + (b[0] - c[0]) * u, c[1] + (b[1] - c[1]) * u + h * v)
    strokes = [((0.28, 0.2), (0.28, 0.82)), ((0.72, 0.2), (0.72, 0.82)), ((0.28, 0.2), (0.72, 0.52)), ((0.72, 0.2), (0.28, 0.52))]
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for p, q in strokes:
        gd.line([face(*p), face(*q)], fill=GOLD_LIGHT + (255,), width=int(s * 0.05))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(s * 0.02)))
    for p, q in strokes:
        d.line([face(*p), face(*q)], fill=GOLD, width=int(s * 0.03))
    # edges
    for poly, col in ((left, DIRT_DARK), (right, STONE_DARK), ([a, b, c, e], GRASS_DARK)):
        d.line(poly + [poly[0]], fill=col, width=max(2, s // 120))
    return img


def background(w: int, h: int, shot: str | None) -> Image.Image:
    """A real screenshot, blurred and darkened, or a night gradient."""
    bg = Image.new("RGB", (w, h), NIGHT)
    if shot and (MEDIA / shot).exists():
        im = Image.open(MEDIA / shot).convert("RGB")
        scale = max(w / im.width, h / im.height)
        im = im.resize((int(im.width * scale) + 1, int(im.height * scale) + 1), Image.LANCZOS)
        im = im.crop(((im.width - w) // 2, (im.height - h) // 2, (im.width - w) // 2 + w, (im.height - h) // 2 + h))
        bg = Image.blend(im.filter(ImageFilter.GaussianBlur(3)), Image.new("RGB", (w, h), NIGHT), 0.42)
    vign = Image.new("L", (w, h), 0)
    vd = ImageDraw.Draw(vign)
    for i in range(40):
        vd.rectangle([i * w // 80, i * h // 80, w - i * w // 80, h - i * h // 80], fill=int(255 * i / 40))
    vign = vign.filter(ImageFilter.GaussianBlur(min(w, h) // 8))
    return Image.composite(bg, Image.new("RGB", (w, h), (0, 0, 0)), vign)


def fit(img: Image.Image, width: int) -> Image.Image:
    return img.resize((width, int(img.height * width / img.width)), Image.LANCZOS)


def card(w: int, h: int, shot: str | None, tagline: str, sub: str) -> Image.Image:
    out = background(w, h, shot).convert("RGBA")
    mark = fit(wordmark(), int(w * 0.62))
    ico = icon(int(h * 0.42))
    total = ico.width + int(w * 0.03) + mark.width
    x = (w - total) // 2
    y = int(h * 0.26)
    out.alpha_composite(ico, (x, y + (mark.height - ico.height) // 2))
    out.alpha_composite(mark, (x + ico.width + int(w * 0.03), y))
    d = ImageDraw.Draw(out)
    f1 = font("bahnschrift.ttf", int(h * 0.075))
    f2 = font("segoeui.ttf", int(h * 0.045))
    ty = y + mark.height + int(h * 0.06)
    tw = d.textlength(tagline, font=f1)
    d.text(((w - tw) / 2, ty), tagline, font=f1, fill=(240, 236, 226))
    sw = d.textlength(sub, font=f2)
    d.text(((w - sw) / 2, ty + int(h * 0.1)), sub, font=f2, fill=(200, 190, 170))
    return out.convert("RGB")


def main():
    MEDIA.mkdir(parents=True, exist_ok=True)
    wm = wordmark()
    wm.save(MEDIA / "logo-wordmark.png")
    icon(512).save(MEDIA / "icon-512.png")
    icon(256).save(MEDIA / "icon-256.png")
    card(1280, 640, "cottage-depth.png", "Real Minecraft, inside Valheim.", "Steve, his blocks, his mobs and his items, depth-correct in the Viking's world").save(MEDIA / "social-preview.png")
    card(1600, 500, "cottage-depth.png", "Real Minecraft, inside Valheim.", "A two-game mod for Windows: Valheim + Minecraft Java 26.3").save(MEDIA / "banner.png")
    print("brand assets ->", MEDIA)


if __name__ == "__main__":
    main()
