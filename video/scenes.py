"""The rendered (non-gameplay) segments of MineHeim's two videos."""
from __future__ import annotations

import math

from PIL import Image, ImageDraw

import brand
from engine import (BRONZE, DIRT, GOLD, GRASS, H, INK, LINE, MUTED, NIGHT, PANEL, RED, SKY, STONE, W, Scene, arrow_sprite,
                    back_out, body, bold, clamp, ease_in_out, ease_out, fade, head, labelled_box, load, mono, place, place_tl,
                    rounded_box, scaled, sprite, text_sprite, window)

CX, CY = W // 2, H // 2

# ------------------------------------------------------------------ shared pieces

_letters = None


def letters():
    global _letters
    if _letters is None:
        word = "MINEHEIM"
        _letters = [brand.wordmark(cell=26, letters=ch, mine_count=1 if i < 4 else 0) for i, ch in enumerate(word)]
    return _letters


def logo_layout(scale=1.0):
    """x offsets of each letter of the wordmark (a gap between MINE and HEIM)."""
    ls = letters()
    gap, word_gap = int(10 * scale), int(60 * scale)
    widths = [int(l.width * scale) for l in ls]
    total = sum(widths) + gap * 6 + word_gap
    xs, x = [], CX - total / 2
    for i, w in enumerate(widths):
        xs.append(x + w / 2)
        x += w + (word_gap if i == 3 else gap)
    return xs


def draw_logo(frame, t, start=0.0, cy=CY - 80, scale=1.0, drop=True):
    """Letters drop in one by one like placed blocks, then the icon pops."""
    ls = letters()
    xs = logo_layout(scale)
    for i, (l, x) in enumerate(zip(ls, xs)):
        k = window(t, start + i * 0.09, 0.45)
        if k <= 0:
            continue
        y = cy - (1 - back_out(k)) * 260 if drop else cy
        place(frame, l, x, y, a=clamp(k * 2), s=scale)


def heading(frame, t, text, start=0.0, y=170, size=96, color=INK):
    k = ease_out(window(t, start, 0.5))
    sp = _cache(("h", text, size, color), lambda: text_sprite(text, head(size), color))
    place(frame, sp, CX, y + (1 - k) * 40, a=k)


def kicker(frame, t, text, start=0.0, y=90, color=GOLD):
    k = ease_out(window(t, start, 0.4))
    sp = _cache(("k", text, color), lambda: text_sprite(text.upper(), bold(34), color))
    place(frame, sp, CX, y, a=k)


def caption(frame, t, text, start, y, size=48, color=INK, width=1900, dur=0.5, x=None, align="center"):
    k = ease_out(window(t, start, dur))
    sp = _cache(("c", text, size, color, width, align), lambda: text_sprite(text, body(size), color, max_width=width, align=align))
    if x is None:
        place(frame, sp, CX, y + (1 - k) * 24, a=k)
    else:
        place_tl(frame, sp, x, y + (1 - k) * 24, a=k)


_sprites: dict = {}


def _cache(key, make):
    if key not in _sprites:
        _sprites[key] = make()
    return _sprites[key]


def fade_out(frame, t, duration, length=0.35):
    """Fade to black over the last `length` seconds."""
    k = window(t, duration - length, length)
    if k > 0:
        frame.alpha_composite(Image.new("RGBA", frame.size, (0, 0, 0, int(255 * k))))


def fade_in(frame, t, length=0.3):
    k = 1 - window(t, 0, length)
    if k > 0:
        frame.alpha_composite(Image.new("RGBA", frame.size, (0, 0, 0, int(255 * k))))


def wrap_scene(name, duration, fn, background="night", alpha=False, fades=True):
    def draw(frame, t):
        fn(frame, t)
        if fades and not alpha:
            fade_in(frame, t)
            fade_out(frame, t, duration)
    return Scene(name, duration, draw, background, alpha)


# ------------------------------------------------------------------ both videos

def logo_sting(duration=4.5, tagline="Real Minecraft, inside Valheim."):
    icon = brand.icon(300)

    def fn(frame, t):
        k = back_out(window(t, 0.95, 0.5))
        if k > 0:
            place(frame, icon, CX, CY - 330, a=clamp(k * 2), s=0.6 + 0.4 * k)
        draw_logo(frame, t, 0.1, cy=CY - 20)
        caption(frame, t, tagline, 1.6, CY + 210, size=64, color=INK)
    return wrap_scene("logo_sting", duration, fn)


def outro(duration=20.0, technical=False):
    """End card: the logo, where to get it, the credits, and room for YouTube's end-screen elements (right half)."""
    icon = brand.icon(200)

    def fn(frame, t):
        k = ease_out(window(t, 0.1, 0.6))
        x0 = 520
        place(frame, icon, x0, 330, a=k)
        mark = _cache("outro_mark", lambda: scaled(brand.wordmark(cell=18), 1.0))
        place(frame, mark, x0 + 40 + mark.width / 2 - 100 + 160, 330, a=k)
        caption(frame, t, "Free and open source", 0.5, 520, size=58, color=GOLD, x=180, align="left", width=1100)
        caption(frame, t, "github.com/Daolyap/MineHeim", 0.8, 610, size=64, color=INK, x=180, align="left", width=1200)
        caption(frame, t, "Windows · Valheim (Steam) · Minecraft Java 26.3 · your own copies of both", 1.1, 720, size=36, color=MUTED, x=180,
                align="left", width=1400)
        lines = ("Built with AI: Claude Code (Anthropic) wrote and tested the code, directed by Daolyap.\n"
                 "Based on rehan-remade/universal-modder (MIT). Uses BepInEx, ReShade, Fabric and Prism Launcher.\n"
                 "Minecraft is a trademark of Mojang Studios / Microsoft, Valheim of Iron Gate AB. Fan project, not affiliated.")
        caption(frame, t, lines, 1.5, 1060, size=30, color=MUTED, x=180, align="left", width=1150)
        if technical:
            caption(frame, t, "Engineering notes and every test: docs/ in the repo", 1.3, 860, size=40, color=INK, x=180, align="left", width=1150)
    return wrap_scene("outro", duration, fn)


def chapter(title, number, subtitle="", duration=3.0):
    def fn(frame, t):
        k = ease_out(window(t, 0.05, 0.5))
        num = _cache(("chn", number), lambda: text_sprite(f"{number:02d}", head(200), GOLD))
        place(frame, num, CX - 700, CY, a=k * 0.9)
        bar = _cache("chbar", lambda: rounded_box(10, 320, fill=GOLD, outline=GOLD, radius=5, width=1))
        place(frame, bar, CX - 530, CY, a=k)
        tt = _cache(("cht", title), lambda: text_sprite(title, head(110), INK))
        place_tl(frame, tt, CX - 480 + (1 - k) * 60, CY - tt.height + 10, a=k)
        if subtitle:
            st = _cache(("chs", subtitle), lambda: text_sprite(subtitle, body(46), MUTED, max_width=1500))
            place_tl(frame, st, CX - 480 + (1 - k) * 60, CY + 20, a=ease_out(window(t, 0.3, 0.5)))
    return wrap_scene(f"chapter_{number:02d}", duration, fn)


def feature_card(number, title, line, shot=None, duration=3.0):
    """Full-frame card before a gameplay clip: the feature's name, one line, and a real screenshot."""
    def fn(frame, t):
        k = ease_out(window(t, 0.05, 0.45))
        big = _cache(("fbig", number), lambda: fade(text_sprite(f"{number:02d}", head(620), (255, 255, 255), shadow=False), 0.05))
        place(frame, big, CX, CY + 40 - (1 - k) * 30, a=k)
        rule = _cache("frule", lambda: rounded_box(360, 8, fill=GOLD, outline=GOLD, radius=4, width=1))
        place(frame, rule, CX, 560 if not shot else 460, a=k, s=0.3 + 0.7 * k)
        kicker(frame, t, f"Feature {number:02d}", 0.0, y=330 if not shot else 250)
        tt = _cache(("ft", title), lambda: text_sprite(title, head(150), INK, max_width=2000, align="center"))
        place(frame, tt, CX, (450 if not shot else 360) + (1 - k) * 50, a=k)
        caption(frame, t, line, 0.25, 660 if not shot else 540, size=58, color=MUTED, width=1700)
        if shot:
            img = _cache(("fshot", shot), lambda: load(shot, width=1240))
            ks = ease_out(window(t, 0.35, 0.6))
            place(frame, img, CX, 980 + (1 - ks) * 80, a=ks)
    return wrap_scene(f"feature_{number:02d}", duration, fn)


def lower_third(title, line, duration=6.0):
    """A transparent overlay (ProRes 4444) to lay over your own gameplay clip: bottom-left title and line."""
    def fn(frame, t):
        k_in = ease_out(window(t, 0.0, 0.45))
        k_out = 1 - ease_in_out(window(t, duration - 0.5, 0.45))
        k = min(k_in, k_out)
        plate = _cache(("lt", title, line), lambda: _lt_plate(title, line))
        place_tl(frame, plate, 120 - (1 - k) * 80, H - plate.height - 130, a=k)
    return Scene(f"lt_{title[:20].replace(' ', '_').lower()}", duration, fn, alpha=True)


def _lt_plate(title, line):
    t = text_sprite(title, head(68), INK, shadow=False)
    l = text_sprite(line, body(38), (222, 214, 198), shadow=False, max_width=1300)
    w = max(t.width, l.width) + 90
    h = t.height + l.height - 6
    plate = rounded_box(w, h, fill=(14, 17, 23), outline=(60, 66, 78), radius=20, accent=GOLD)
    plate.alpha_composite(t, (44, 8))
    plate.alpha_composite(l, (44, t.height - 18))
    return plate


# ------------------------------------------------------------------ general video

def hook(duration=4.0):
    def fn(frame, t):
        caption(frame, t, "This is real Minecraft.", 0.15, CY - 120, size=110, color=INK)
        caption(frame, t, "Running inside Valheim.", 1.3, CY + 40, size=110, color=GOLD)
    return wrap_scene("hook", duration, fn)


def explainer_simple(duration=26.0):
    """Two games, one world: what each sends the other, and how the picture is merged (depth)."""
    vbox = labelled_box(640, 220, "Valheim", "the world, the Viking, the camera", accent=BRONZE)
    mbox = labelled_box(640, 220, "Minecraft", "the real game, on your account", accent=GRASS)
    lx, rx, by = 700, W - 700, 430
    a1, a1p = arrow_sprite(lx + 340, by - 40, rx - 340, by - 40, color=GOLD, width=10)
    a2, a2p = arrow_sprite(rx - 340, by + 60, lx + 340, by + 60, color=GRASS, width=10)
    l1 = text_sprite("camera, ground, buildings, creatures", body(38), GOLD)
    l2 = text_sprite("its picture, with depth", body(38), GRASS)
    shot = load("cottage-depth.png", width=1150)

    def layer_card(color, label):
        def draw(d, img, s):
            d.rounded_rectangle([0, 0, 520 * s, 300 * s], radius=22 * s, fill=color + (230,), outline=(255, 255, 255, 60), width=3 * s)
        c = sprite(520, 300, draw)
        tx = text_sprite(label, head(44), INK, shadow=False)
        c.alpha_composite(tx, ((c.width - tx.width) // 2, (c.height - tx.height) // 2))
        return c
    lv, lm = layer_card((70, 58, 40), "Valheim's picture"), layer_card((40, 74, 34), "Minecraft's picture")

    def fn(frame, t):
        heading(frame, t, "Two real games. One world.", 0.0, y=150)
        place(frame, vbox, lx, by, a=ease_out(window(t, 0.6, 0.5)))
        place(frame, mbox, rx, by, a=ease_out(window(t, 1.0, 0.5)))
        k1 = ease_out(window(t, 2.0, 0.6))
        place_tl(frame, a1, *a1p, a=k1)
        place(frame, l1, CX, by - 110, a=k1)
        k2 = ease_out(window(t, 5.0, 0.6))
        place_tl(frame, a2, *a2p, a=k2)
        place(frame, l2, CX, by + 130, a=k2)
        # the merge: two layers slide together, then the real result
        km = ease_in_out(window(t, 9.0, 1.4))
        if t > 8.5:
            a = ease_out(window(t, 8.5, 0.5)) * (1 - ease_out(window(t, 15.0, 0.6)))
            place(frame, lv, CX - 420 + 300 * km, 960 - 30 * km, a=a)
            place(frame, lm, CX + 420 - 300 * km, 960 + 30 * km, a=a)
            caption(frame, t, "every pixel shows whichever world is nearer", 10.6, 1250, size=46, color=INK)
        if t > 15.0:
            place(frame, shot, CX, 1000, a=ease_out(window(t, 15.0, 0.7)))
            caption(frame, t, "Valheim's trees in front of a Minecraft cottage", 16.0, 1370, size=40, color=MUTED)
    return wrap_scene("explainer", duration, fn)


def install_steps(duration=24.0):
    steps = [("1", "Download and unzip", "MineHeim-<version>.zip from GitHub", None),
             ("2", "Run Install.cmd", "Valheim's mod loader, the compositor and Prism Launcher: official downloads, hash-checked",
              "install-console.png"),
             ("3", "Sign in to Minecraft once", "with the Microsoft account that owns it, in Prism Launcher", "install-prism-setup.png"),
             ("4", "Start Valheim", "Minecraft starts by itself. F7 hands it the mouse.", "minecraft-inventory.png")]

    def fn(frame, t):
        heading(frame, t, "Install in four steps", 0.0, y=140)
        seg = (duration - 2.0) / len(steps)
        for i, (n, title, line, shot) in enumerate(steps):
            start = 0.6 + i * seg
            k = ease_out(window(t, start, 0.5))
            y = 330 + i * 240
            num = _cache(("in", n), lambda n=n: _num_badge(n))
            place(frame, num, 250, y + 40, a=k)
            tt = _cache(("it", title), lambda title=title: text_sprite(title, head(66), INK))
            place_tl(frame, tt, 330 + (1 - k) * 40, y - 10, a=k)
            ll = _cache(("il", line), lambda line=line: text_sprite(line, body(38), MUTED, max_width=1050))
            place_tl(frame, ll, 330 + (1 - k) * 40, y + 70, a=k)
            if shot:
                active = window(t, start, 0.5) * (1 - window(t, start + seg, 0.4) if i < len(steps) - 1 else 1)
                if active > 0:
                    img = _cache(("ish", shot), lambda shot=shot: load(shot, width=1050))
                    place(frame, img, 1940, 820, a=ease_out(active))
    return wrap_scene("install", duration, fn)


def _num_badge(n):
    def draw(d, img, s):
        d.ellipse([0, 0, 120 * s, 120 * s], fill=GOLD)
    b = sprite(120, 120, draw)
    tx = text_sprite(n, head(72), NIGHT, shadow=False)
    b.alpha_composite(tx, ((b.width - tx.width) // 2, (b.height - tx.height) // 2 - 4))
    return b


# ------------------------------------------------------------------ technical video

def tech_title(duration=8.0):
    chapters = ["Architecture", "One camera", "One picture", "One ground", "One fight", "Items that move", "Light and screens",
                "Testing", "Built with AI"]

    def fn(frame, t):
        heading(frame, t, "How MineHeim works", 0.0, y=230, size=130)
        caption(frame, t, "the technical breakdown", 0.4, 360, size=54, color=GOLD)
        for i, c in enumerate(chapters):
            col, row = i % 3, i // 3
            k = ease_out(window(t, 1.0 + i * 0.12, 0.4))
            sp = _cache(("tc", c), lambda c=c, i=i: text_sprite(f"{i + 1:02d}  {c}", body(48), INK))
            place_tl(frame, sp, 380 + col * 640, 620 + row * 140 + (1 - k) * 20, a=k)
    return wrap_scene("tech_title", duration, fn)


def architecture(duration=40.0):
    """The two processes, the socket, the shared memories and the add-on, built up step by step."""
    vproc = rounded_box(1060, 860, fill=(30, 26, 22), outline=BRONZE, radius=28, width=4)
    mproc = rounded_box(760, 860, fill=(22, 30, 22), outline=GRASS, radius=28, width=4)
    vt = text_sprite("Valheim process  (Unity 6, Direct3D 11)", head(40), BRONZE, shadow=False)
    mt = text_sprite("Minecraft process  (Java, Fabric)", head(40), GRASS, shadow=False)
    plugin = labelled_box(700, 190, "BepInEx plugin", "MineHeim.dll: camera, terrain, events", accent=BRONZE)
    addon = labelled_box(700, 190, "ReShade add-on + effect", "MineHeim.addon64 + MineHeim.fx", accent=GOLD)
    mod = labelled_box(560, 190, "Fabric mod", "mineheim.jar", accent=GRASS)
    render_box = labelled_box(560, 190, "Minecraft's renderer", "colour, depth, overlay", accent=GRASS)
    vx, mx, top = 260, 1560, 300
    a_ws, p_ws = arrow_sprite(vx + 840, top + 230, mx + 120, top + 230, color=GOLD, width=8)
    a_ws2, p_ws2 = arrow_sprite(mx + 120, top + 300, vx + 840, top + 300, color=GRASS, width=8)
    a_shm, p_shm = arrow_sprite(mx + 120, top + 610, vx + 840, top + 610, color=SKY, width=12)
    a_pose, p_pose = arrow_sprite(vx + 470, top + 330, vx + 470, top + 500, color=SKY, width=8, dashed=True)
    l_ws = text_sprite("WebSocket, JSON, 127.0.0.1:25599", mono(34), GOLD)
    l_ws2 = text_sprite("events back", mono(30), GRASS)
    l_shm = text_sprite("shared memory: Local\\MineHeimFrame", mono(32), SKY)
    l_pose = text_sprite("pose of the frame\nbeing drawn", mono(28), SKY)
    screen = labelled_box(700, 120, "Your screen", "", accent=INK)

    def fn(frame, t):
        heading(frame, t, "Architecture", 0.0, y=120)
        k = ease_out(window(t, 0.5, 0.6))
        place_tl(frame, vproc, vx, top, a=k)
        place_tl(frame, vt, vx + 30, top + 18, a=k)
        k2 = ease_out(window(t, 1.2, 0.6))
        place_tl(frame, mproc, mx, top, a=k2)
        place_tl(frame, mt, mx + 30, top + 18, a=k2)
        place_tl(frame, plugin, vx + 120, top + 160, a=ease_out(window(t, 2.5, 0.5)))
        place_tl(frame, mod, mx + 100, top + 160, a=ease_out(window(t, 3.2, 0.5)))
        kw = ease_out(window(t, 5.0, 0.6))
        place_tl(frame, a_ws, *p_ws, a=kw)
        place(frame, l_ws, (vx + 840 + mx + 120) / 2, top + 180, a=kw)
        kw2 = ease_out(window(t, 9.0, 0.6))
        place_tl(frame, a_ws2, *p_ws2, a=kw2)
        place(frame, l_ws2, (vx + 840 + mx + 120) / 2, top + 345, a=kw2)
        place_tl(frame, render_box, mx + 100, top + 520, a=ease_out(window(t, 13.0, 0.5)))
        place_tl(frame, addon, vx + 120, top + 520, a=ease_out(window(t, 16.0, 0.5)))
        ks = ease_out(window(t, 18.0, 0.6))
        place_tl(frame, a_shm, *p_shm, a=ks)
        place(frame, l_shm, (vx + 840 + mx + 120) / 2, top + 560, a=ks)
        kp = ease_out(window(t, 23.0, 0.6))
        place_tl(frame, a_pose, *p_pose, a=kp)
        place_tl(frame, l_pose, vx + 500, top + 380, a=kp)
        place(frame, screen, vx + 470, top + 790 + 160, a=ease_out(window(t, 28.0, 0.5)))
        caption(frame, t, "Valheim is the host: its camera, terrain, health and saves are the truth.", 31.0, 1380, size=40, color=MUTED)
    return wrap_scene("architecture", duration, fn)


def mapping(duration=24.0):
    formula = text_sprite("Minecraft (x, y, z)  =  Valheim (x, y + 70, -z)", mono(70), GOLD)
    why1 = "Valheim's z points north, Minecraft's south: one axis flips."
    why2 = "+70 puts Valheim's sea level (y 30) at Minecraft y 100, fixed forever: Minecraft's world persists, so your builds stay on the same hill."
    why3 = "Checked against Minecraft's own camera on 50,000 random orientations: worst error 0.0000000000000048."

    def axes(frame, cx, cy, flip, a):
        def draw(d, img, s):
            o = (300 * s, 300 * s)
            d.line([o, (560 * s, 300 * s)], fill=RED, width=8 * s)
            d.line([o, (300 * s, 40 * s)], fill=GRASS, width=8 * s)
            zx, zy = (130 * s, 450 * s) if not flip else (470 * s, 150 * s)
            d.line([o, (zx, zy)], fill=SKY, width=8 * s)
        ax = _cache(("ax", flip), lambda: sprite(600, 600, draw))
        place(frame, ax, cx, cy, a=a)
        for txt, dx, dy, col in (("x", 280, 0, RED), ("y", 0, -280, GRASS), ("z", -190 if not flip else 190, 170 if not flip else -170, SKY)):
            lab = _cache(("axl", txt, col), lambda txt=txt, col=col: text_sprite(txt, head(60), col))
            place(frame, lab, cx + dx, cy + dy, a=a)

    def fn(frame, t):
        heading(frame, t, "One camera, two coordinate systems", 0.0, y=130)
        ka = ease_out(window(t, 0.6, 0.6))
        axes(frame, 760, 640, False, ka)
        cap1 = _cache("valh", lambda: text_sprite("Valheim", head(56), BRONZE))
        place(frame, cap1, 760, 990, a=ka)
        kb = ease_out(window(t, 1.6, 0.6))
        axes(frame, W - 760, 640, True, kb)
        cap2 = _cache("mc", lambda: text_sprite("Minecraft", head(56), GRASS))
        place(frame, cap2, W - 760, 990, a=kb)
        place(frame, formula, CX, 1120, a=ease_out(window(t, 3.5, 0.6)))
        caption(frame, t, why1, 6.0, 1230, size=40, color=INK)
        if t > 12:
            caption(frame, t, why2, 12.0, 1310, size=36, color=MUTED, width=2200)
        if t > 18:
            caption(frame, t, why3, 18.0, 1380, size=36, color=GOLD, width=2200)
    return wrap_scene("mapping", duration, fn)


def picture(duration=46.0):
    """Layers, the depth test, and re-projection with the measured numbers."""
    def layer(color, label, sub):
        def draw(d, img, s):
            d.rounded_rectangle([0, 0, 640 * s, 380 * s], radius=22 * s, fill=color + (240,), outline=(255, 255, 255, 50), width=3 * s)
        c = sprite(640, 380, draw)
        tx = text_sprite(label, head(48), INK, shadow=False)
        sx = text_sprite(sub, body(30), (225, 220, 210), shadow=False, max_width=560, align="center")
        c.alpha_composite(tx, ((c.width - tx.width) // 2, 120))
        c.alpha_composite(sx, ((c.width - sx.width) // 2, 200))
        return c
    layers = [layer((52, 74, 44), "colour", "what Minecraft sees"), layer((40, 46, 64), "depth", "how far each pixel is"),
              layer((70, 60, 34), "overlay", "hand, hotbar, open screens")]
    bars = [("with re-projection", 1.2, 3.0, GRASS), ("without", 5.6, 14.0, RED)]
    shot = load("cottage-depth.png", width=1000)

    def fn(frame, t):
        heading(frame, t, "One picture", 0.0, y=120)
        # 1. three layers fan out
        k = ease_out(window(t, 0.6, 0.8)) * (1 - ease_out(window(t, 13.5, 0.6)))
        for i, l in enumerate(layers):
            place(frame, l, CX + (i - 1) * 720 * k, 560 + i * 18 * (1 - k), a=min(1, k * 1.5))
        if t < 13.5:
            caption(frame, t, "Minecraft renders three layers into shared memory, every frame.", 2.0, 880, size=44, color=INK)
        if t < 14:
            caption(frame, t, "Inside Valheim, a ReShade add-on picks up the newest one.", 5.5, 960, size=40, color=MUTED)
        # 2. depth test
        if 13.5 < t < 29.0:
            kd = ease_out(window(t, 14.0, 0.6)) * (1 - ease_out(window(t, 29.0, 0.6)))
            place(frame, shot, 820, 700, a=kd)
            caption(frame, t, "Depth test: each pixel shows whichever world is nearer.", 15.0, 470, size=46, color=INK, x=1420, width=980, align="left")
            caption(frame, t, "Valheim's own depth buffer (reversed-Z, upside down in D3D11) against Minecraft's.", 17.5, 680, size=38, color=MUTED, x=1420, width=980, align="left")
            caption(frame, t, "That is why grass grows over a Minecraft wall and a tree hides half a cottage.", 21.0, 880, size=38, color=GOLD, x=1420, width=980, align="left")
        # 3. re-projection numbers
        if t > 29:
            kr = ease_out(window(t, 29.5, 0.6))
            caption(frame, t, "Minecraft's frame is 1-3 frames old. The shader re-projects it to Valheim's newest camera.", 29.5, 420, size=46, color=INK, width=2000)
            caption(frame, t, "Edge error while strafing (pixels):", 32.0, 560, size=40, color=MUTED)
            for i, (label, mean, worst, col) in enumerate(bars):
                y = 720 + i * 220
                kb = ease_out(window(t, 33.0 + i * 0.8, 1.0))
                lab = _cache(("bl", label), lambda label=label: text_sprite(label, head(48), INK))
                place_tl(frame, lab, 360, y - 30, a=kr)
                bw = int(1000 * (worst / 14.0) * kb)
                mw = int(1000 * (mean / 14.0) * kb)
                if bw > 2:
                    bar = Image.new("RGBA", (bw, 70), col + (90,))
                    frame.alpha_composite(bar, (900, y - 30))
                if mw > 2:
                    frame.alpha_composite(Image.new("RGBA", (mw, 70), col + (255,)), (900, y - 30))
                num = _cache(("bn", label), lambda mean=mean, worst=worst: text_sprite(f"mean {mean}   worst {worst}", mono(40), INK))
                place_tl(frame, num, 920 + max(bw, mw) + 20, y - 20, a=kb)
    return wrap_scene("picture", duration, fn)


def ground(duration=40.0):
    """Raycast columns become barrier cells; a Valheim wall becomes a block of cells; Minecraft blocks become colliders."""
    def fn(frame, t):
        heading(frame, t, "One ground, both ways", 0.0, y=120)
        layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        base_y, x0, cw = 960, 360, 72
        # terrain profile
        hs = [3.2 + 1.4 * math.sin(i * 0.45) + 0.6 * math.sin(i * 1.3) for i in range(24)]
        kt = ease_out(window(t, 0.4, 0.8))
        pts = [(x0 + i * cw + cw / 2, base_y - h * 60) for i, h in enumerate(hs)]
        if kt > 0:
            poly = [(x0, base_y + 120)] + pts + [(x0 + 24 * cw, base_y + 120)]
            d.polygon(poly, fill=(58, 82, 44, int(255 * kt)))
        # rays fall one by one
        for i, (px, py) in enumerate(pts):
            kr = window(t, 2.0 + i * 0.12, 0.25)
            if kr > 0:
                d.line([(px, 300), (px, 300 + (py - 300) * kr)], fill=GOLD + (int(150 * (1 - window(t, 6.5, 1.0))),), width=4)
            kb = ease_out(window(t, 2.3 + i * 0.12, 0.3))
            if kb > 0:
                top = math.floor(py / 60) * 60
                for c in range(2):
                    y = top + c * 60 + 2
                    d.rectangle([px - cw / 2 + 3, y, px + cw / 2 - 3, y + 56], fill=(120, 150, 210, int(150 * kb)), outline=(170, 200, 255, int(220 * kb)), width=2)
        caption(frame, t, "Raycasts down Valheim's terrain: two invisible barrier blocks just under each column's surface.", 2.5, 1250, size=42, color=INK, width=2200)
        # a building piece voxelized
        if t > 13:
            kw = ease_out(window(t, 13.0, 0.6))
            wx, wy = 1700, 560
            d.rectangle([wx, wy, wx + 30, wy + 300], fill=BRONZE + (int(255 * kw),))
            for gy in range(5):
                kc = ease_out(window(t, 15.0 + gy * 0.25, 0.3))
                if kc > 0:
                    d.rectangle([wx - 30, wy + gy * 60, wx + 60 - 2, wy + gy * 60 + 58], outline=(170, 200, 255, int(255 * kc)), width=4)
            frame.alpha_composite(layer)
            layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
            d = ImageDraw.Draw(layer)
            caption(frame, t, "Valheim's buildings: every piece is tested cell by cell against its own colliders.", 14.0, 300, size=40, color=INK, x=1240, width=1200, align="left")
            caption(frame, t, "Ground and buildings share reference-counted cells. A door that opens is voxelized again.", 18.0, 420, size=36, color=MUTED, x=1240, width=1200, align="left")
        frame.alpha_composite(layer)
        if t > 26:
            caption(frame, t, "And back: every solid Minecraft block near you becomes a 1 m box collider in Valheim.", 26.0, 1340, size=42, color=GOLD, width=2200)
    return wrap_scene("ground", duration, fn)


def fight(duration=48.0):
    def fn(frame, t):
        heading(frame, t, "One fight", 0.0, y=120)
        left = _cache("fl", lambda: labelled_box(900, 300, "Valheim creature  ->  Minecraft", "an invisible mineheim:creature entity of its size: monsters target it, their hits come back", accent=BRONZE, sub_size=32))
        right = _cache("fr", lambda: labelled_box(900, 300, "Minecraft mob  ->  Valheim", "an invisible body cloned from a creature prefab: Valheim's creatures attack it, the Viking can hit it", accent=GRASS, sub_size=32))
        place(frame, left, 760, 430, a=ease_out(window(t, 0.6, 0.6)))
        place(frame, right, W - 760, 430, a=ease_out(window(t, 2.5, 0.6)))
        formula = _cache("ff", lambda: text_sprite("1 Minecraft point  =  Viking's max health / 20", mono(64), GOLD))
        place(frame, formula, CX, 760, a=ease_out(window(t, 9.0, 0.6)))
        rows = [("zombie's 3", "15% of the Viking, whatever his health", 12.0),
                ("diamond sword, 7", "35 Valheim damage (100-health Viking)", 14.5),
                ("Troll's 70", "14 Minecraft damage to a zombie", 17.0)]
        for i, (a, b, s) in enumerate(rows):
            k = ease_out(window(t, s, 0.5))
            sa = _cache(("fa", a), lambda a=a: text_sprite(a, head(50), INK))
            sb = _cache(("fb", b), lambda b=b: text_sprite(b, body(44), MUTED))
            place_tl(frame, sa, 520, 880 + i * 110, a=k)
            place_tl(frame, sb, 1180, 886 + i * 110, a=k)
        caption(frame, t, "Damage always goes through each game's own code: armour, blocking and AI react as usual.", 22.0, 1300, size=40, color=INK, width=2200)
        if t > 30:
            caption(frame, t, "Bug found in testing: hits on bodies counted as backstabs (x3) and included tree-chopping damage. Fixed.", 30.0, 1380, size=34, color=RED, width=2200)
    return wrap_scene("fight", duration, fn)


def items(duration=38.0):
    """The take / taken handshake as an animated sequence diagram."""
    cols = [("Steve", GRASS), ("Minecraft mod", GRASS), ("Valheim plugin", BRONZE), ("Viking", BRONZE)]
    xs = [420, 1000, 1580, 2160]
    steps = [(0, 1, "throws 6 oak planks (Q)", 2.0), (1, 2, "itemout: oak_planks x6", 5.0), (2, 2, "4 planks = 1 Wood -> take 4", 8.0),
             (2, 1, "itemtake: 4", 11.0), (1, 1, "still there? shrink the stack to 2", 14.0), (1, 2, "itemtaken: 4", 17.0),
             (2, 3, "1 Wood lands; the Viking picks it up", 20.0)]

    def fn(frame, t):
        heading(frame, t, "Items that move, never copy", 0.0, y=120)
        layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        labels = []
        for (name, col), x in zip(cols, xs):
            k = ease_out(window(t, 0.4, 0.5))
            hb = _cache(("ih", name), lambda name=name, col=col: labelled_box(440, 100, name, "", accent=col, title_size=40))
            labels.append((hb, x, 280, k))
            if k > 0:
                d.line([(x, 340), (x, 1180)], fill=(110, 118, 132, int(200 * k)), width=3)
        for i, (a, b, label, s) in enumerate(steps):
            k = ease_out(window(t, s, 0.6))
            if k <= 0:
                continue
            y = 400 + i * 110
            if a == b:
                d.rounded_rectangle([xs[a] - 30, y - 26, xs[a] + 30, y + 26], radius=10, outline=GOLD + (int(255 * k),), width=4)
                lab = _cache(("il2", label), lambda label=label: text_sprite(label, body(36), INK))
                labels.append((lab, xs[a] + 50 + lab.width / 2, y - 26 + lab.height / 2, k))
            else:
                x1 = xs[a] + (xs[b] - xs[a]) * k
                d.line([(xs[a], y), (x1, y)], fill=GOLD + (255,), width=6)
                sgn = 1 if xs[b] > xs[a] else -1
                d.polygon([(x1, y), (x1 - sgn * 26, y - 14), (x1 - sgn * 26, y + 14)], fill=GOLD)
                lab = _cache(("il2", label), lambda label=label: text_sprite(label, body(36), INK))
                labels.append((lab, (xs[a] + xs[b]) / 2, y - 34, k))
        frame.alpha_composite(layer)
        for img, x, y, k in labels:
            place(frame, img, x, y, a=k)
        caption(frame, t, "Valheim creates the item only after Minecraft took it away: an item picked up in between never crosses.", 24.0, 1290, size=40, color=GOLD, width=2200)
        caption(frame, t, "~45 pairs built in (Wood = oak log, Stone = cobblestone, Iron = iron ingot...), plus your own.", 29.0, 1370, size=36, color=MUTED, width=2200)
    return wrap_scene("items", duration, fn)


def light_and_screens(duration=34.0):
    def fn(frame, t):
        heading(frame, t, "Light, screens, start and stop", 0.0, y=120)
        rows = [("Time", "Minecraft's clock follows Valheim's day: its blocks dim at dusk, go dark at night.", GOLD),
                ("Fire", "Valheim's burning fires and torches become invisible Minecraft light blocks.", BRONZE),
                ("Lamps", "Minecraft's torches, lanterns and lava become Unity point lights: the nearest dozen.", GRASS),
                ("Screens", "Inventory, creative tabs and chat: cursor, clicks and typed text forwarded. Minecraft 26 is SDL3: keys are USB scancodes (E = 8, Esc = 41), not GLFW.", SKY),
                ("Start / stop", "No Minecraft answering? Valheim runs Prism Launcher --launch MineHeim. Its window waits behind Valheim; it saves and quits with Valheim.", INK)]
        for i, (a, b, col) in enumerate(rows):
            k = ease_out(window(t, 0.8 + i * 5.5, 0.6))
            ta = _cache(("la", a), lambda a=a, col=col: text_sprite(a, head(58), col))
            tb = _cache(("lb", b), lambda b=b: text_sprite(b, body(40), INK, max_width=1600))
            place_tl(frame, ta, 260, 300 + i * 210, a=k)
            place_tl(frame, tb, 760, 306 + i * 210, a=k)
    return wrap_scene("light_screens", duration, fn)


def testing(duration=30.0):
    stats = [("50,000", "random camera orientations vs Minecraft's own maths", "worst error 4.8e-15"),
             ("~1 px", "alignment of a Minecraft pillar vs Unity's projection", "in the real games"),
             ("19 / 19", "real-game checks for items, mobs, light, buildings", "0.4.0 suite on 0.9.0"),
             ("6 / 6", "real-game checks for screens, auto-start, F8", "0.9.0 suite")]

    def fn(frame, t):
        heading(frame, t, "Built against oracles, not eyeballing", 0.0, y=120)
        for i, (big, line, small) in enumerate(stats):
            col, row = i % 2, i // 2
            k = ease_out(window(t, 0.8 + i * 1.6, 0.6))
            x, y = 330 + col * 1000, 330 + row * 420
            b = _cache(("tb", big), lambda big=big: text_sprite(big, head(140), GOLD))
            place_tl(frame, b, x, y + (1 - k) * 30, a=k)
            l1 = _cache(("tl", line), lambda line=line: text_sprite(line, body(40), INK, max_width=900))
            place_tl(frame, l1, x, y + 170, a=k)
            l2 = _cache(("ts", small), lambda small=small: text_sprite(small, body(34), MUTED))
            place_tl(frame, l2, x, y + 300, a=k)
        caption(frame, t, "Both games driven by scripts through plugin test hooks. Every failure got a number before a fix.", 9.0, 1250, size=40, color=INK, width=2200)
    return wrap_scene("testing", duration, fn)


def built_with_ai(duration=26.0):
    def fn(frame, t):
        heading(frame, t, "Built with AI", 0.0, y=150)
        lines = [("Claude Code (Anthropic) wrote and tested the code.", INK, 1.0),
                 ("Daolyap set the goal and the rules, played it, and made the calls.", INK, 4.0),
                 ("Rules it worked under: single-player only, never get around Minecraft's ownership check,", MUTED, 8.0),
                 ("ship no game files or decompiled code, back up first, ask before touching the machine.", MUTED, 10.0),
                 ("Started from rehan-remade/universal-modder (MIT). Stands on BepInEx, ReShade, Fabric, Prism Launcher.", GOLD, 14.0)]
        for i, (txt, col, s) in enumerate(lines):
            caption(frame, t, txt, s, 380 + i * 150, size=50 if i < 2 else 42, color=col, width=2200)
    return wrap_scene("built_with_ai", duration, fn)


# ------------------------------------------------------------------ thumbnails (still images)

def thumbnail(path, shot, big, small):
    from engine import MEDIA
    from PIL import ImageFilter
    w, h = 1280, 720
    im = Image.open(MEDIA / shot).convert("RGB")
    s = max(w / im.width, h / im.height)
    im = im.resize((int(im.width * s) + 2, int(im.height * s) + 2), Image.LANCZOS).crop((0, 0, w, h)).convert("RGBA")
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(shade).rectangle([0, 0, w * 0.62, h], fill=(0, 0, 0, 150))
    im.alpha_composite(shade.filter(ImageFilter.GaussianBlur(60)))
    tb = text_sprite(big, head(118), INK, max_width=760)
    ts = text_sprite(small, head(64), GOLD, max_width=760)
    im.alpha_composite(tb, (40, 120))
    im.alpha_composite(ts, (44, 120 + tb.height))
    ic = brand.icon(150)
    im.alpha_composite(ic, (w - 190, 30))
    im.convert("RGB").save(path, quality=92)
