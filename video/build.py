"""Render MineHeim's video kit.

    python video/build.py stills            contact sheets of every rendered segment (quick look, no video)
    python video/build.py general|technical  render that video: segments, slot slates, overlays, full-length draft, script
    python video/build.py all                both videos and the thumbnails
    python video/build.py sound [video]      only the synthesised soundtrack, muxed onto drafts already rendered

Output in video/out/<video>/ (gitignored): NN_<name>.mp4 segments, NN_slot_<id>.mp4 placeholder slates, overlays/*.mov
(transparent lower thirds, ProRes 4444), <video>_draft.mp4 (everything in order, slates where your footage goes) and
<video>_script.md (timestamps, voiceover, and the footage each slot needs)."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine  # noqa: E402
import scenes  # noqa: E402
from engine import FPS, GOLD, H, INK, MUTED, OUT, W, body, head, mono, text_sprite  # noqa: E402
from timeline import VIDEOS, Seg, Slot  # noqa: E402


def slate(slot: Slot) -> Image.Image:
    img = Image.new("RGBA", (W, H), (18, 20, 26, 255))
    stripes = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(stripes)
    for x in range(-H, W, 120):
        d.polygon([(x, 0), (x + 60, 0), (x + 60 + H, H), (x + H, H)], fill=(255, 255, 255, 10))
    d.rectangle([60, 60, W - 60, H - 60], outline=GOLD + (160,), width=6)
    img.alpha_composite(stripes)
    tag = text_sprite(f"YOUR GAMEPLAY  ·  {slot.number}", head(64), GOLD)
    img.alpha_composite(tag, (140, 130))
    title = text_sprite(slot.title, head(140), INK)
    img.alpha_composite(title, (140, 240))
    secs = text_sprite(f"{slot.seconds:g} seconds", mono(56), MUTED)
    img.alpha_composite(secs, (150, 430))
    foot = text_sprite(slot.footage, body(46), INK, max_width=W - 380)
    img.alpha_composite(foot, (150, 560))
    if slot.overlay:
        ov = text_sprite(f"Lay overlays/{overlay_name(slot)}.mov over it.", body(40), GOLD, max_width=W - 380)
        img.alpha_composite(ov, (150, H - 260))
    return img


def overlay_name(slot: Slot) -> str:
    return f"{slot.number}_lower_third"


def encode_still(img: Image.Image, seconds: float, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    png = path.with_suffix(".png")
    img.convert("RGB").save(png)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-loop", "1", "-framerate", str(FPS), "-i", str(png), "-t", f"{seconds}",
                    "-c:v", "libx264", "-preset", "medium", "-tune", "stillimage", "-crf", "16", "-pix_fmt", "yuv420p", "-r", str(FPS), str(path)], check=True)
    png.unlink()


def timestamp(t: float) -> str:
    return f"{int(t // 60)}:{int(t % 60):02d}"


def build(name: str, only_stills=False):
    items = VIDEOS[name]()
    out = OUT / name
    out.mkdir(parents=True, exist_ok=True)
    files, lines, t = [], [f"# {name.title()} video: script and shot list", "",
                           "Timestamps are for the draft as rendered; your clips can run longer or shorter. Slots (G/T numbers) are where your "
                           "gameplay goes: the footnote under each says what to record.", ""], 0.0
    sheet = []
    for i, it in enumerate(items, 1):
        if isinstance(it, Seg):
            sc = it.scene
            f = out / f"{i:02d}_{sc.name}.mp4"
            if only_stills:
                sheet.append(engine.still(sc, sc.duration * 0.7).convert("RGB").resize((W // 5, H // 5)))
            elif not f.exists():
                print(f"render {f.name} ({sc.duration:g} s)", flush=True)
                engine.render(sc, f)
            dur = sc.duration
            lines.append(f"**{timestamp(t)} - {timestamp(t + dur)}  ·  {sc.name.replace('_', ' ')}** (rendered: `{f.name}`)")
        else:
            f = out / f"{i:02d}_slot_{it.number}.mp4"
            if only_stills:
                sheet.append(slate(it).convert("RGB").resize((W // 5, H // 5)))
            elif not f.exists():
                print(f"slate {f.name}", flush=True)
                encode_still(slate(it), it.seconds, f)
                if it.overlay:
                    ov = scenes.lower_third(*it.overlay, duration=min(it.seconds, 7.0))
                    engine.render(ov, out / "overlays" / f"{overlay_name(it)}.mov")
            dur = it.seconds
            lines.append(f"**{timestamp(t)} - {timestamp(t + dur)}  ·  [{it.number}] YOUR GAMEPLAY: {it.title}** ({dur:g} s)[^{it.number}]")
        vo = it.vo
        lines.append(f"> VO: {vo}" if vo else "> VO: (none: music and game sound)")
        lines.append("")
        files.append(f)
        t += dur
    lines.append(f"Total: {timestamp(t)}")
    lines.append("")
    for it in items:
        if isinstance(it, Slot):
            ov = f" Overlay: `overlays/{overlay_name(it)}.mov` ({it.overlay[0]})." if it.overlay else ""
            lines.append(f"[^{it.number}]: {it.footage}{ov}")
    (out / f"{name}_script.md").write_text("\n".join(lines), encoding="utf-8")
    if only_stills:
        cols = 4
        rows = (len(sheet) + cols - 1) // cols
        cs = Image.new("RGB", (cols * (W // 5), rows * (H // 5)), (0, 0, 0))
        for k, im in enumerate(sheet):
            cs.paste(im, ((k % cols) * (W // 5), (k // cols) * (H // 5)))
        cs.save(out / f"{name}_contact.jpg", quality=85)
        print("contact sheet ->", out / f"{name}_contact.jpg")
        return
    lst = out / "concat.txt"
    lst.write_text("".join(f"file '{f.name}'\n" for f in files), encoding="utf-8")
    draft = out / f"{name}_draft.mp4"
    print("joining", draft.name, flush=True)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(lst),
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-r", str(FPS), "-movflags", "+faststart", str(draft)],
                   check=True, cwd=out)
    soundtrack(name, items, out, draft)
    print(f"{name}: {timestamp(t)} -> {draft}")


# ------------------------------------------------------------------ the soundtrack (video/audio.py: synthesised, no samples)

# how busy the music is under each kind of segment (0 calm .. 1 full): explainers stay quiet, montages and stings lift
ENERGY = {"hook": 0.9, "logo": 1.0, "feature": 0.8, "chapter": 0.55, "outro": 0.7, "tech_title": 0.6, "built_with_ai": 0.4}


def soundtrack(name: str, items, out: Path, draft: Path):
    """Music + sound effects for the whole video, ducked under every voiceover line, muxed into the draft."""
    import audio
    spans, cues, t = [], [], 0.0
    for i, it in enumerate(items):
        dur = it.scene.duration if isinstance(it, Seg) else it.seconds
        kind = it.scene.name if isinstance(it, Seg) else "slot"
        e = next((v for k, v in ENERGY.items() if kind.startswith(k)), 0.85 if kind == "slot" and name == "general" else 0.3)
        spans.append((t, t + dur, e))
        if i > 0:
            cues.append((max(0.0, t - 0.35), "whoosh", 0.5))
        if kind.startswith("logo"):
            cues += [(t + 0.15, "boom", 0.35), (t + 0.25, "chime", 0.8)]
        elif kind.startswith("feature"):
            cues += [(t + 0.1, "tick", 0.8), (t + 0.15, "thump", 0.6)]
        elif kind.startswith("chapter"):
            cues.append((t + 0.1, "chime", 0.35))
        elif kind.startswith("hook"):
            cues.append((t, "thump", 0.8))
        if it.vo:
            words = len(it.vo.split())
            it._vo_span = (t + 0.2, t + 0.2 + min(dur - 0.2, words / 2.6))  # ~156 words a minute
        t += dur
    speech = [it._vo_span for it in items if getattr(it, "_vo_span", None)]

    def energy(x):
        for a, b, e in spans:
            if a <= x < b:
                return e
        return 0.3

    wav = out / f"{name}_soundtrack.wav"
    print("soundtrack", wav.name, flush=True)
    audio.write_wav(wav, audio.mix_track(t, energy, cues, speech))
    muxed = out / f"{name}_draft_av.mp4"
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(draft), "-i", str(wav), "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac",
                    "-b:a", "192k", "-shortest", "-movflags", "+faststart", str(muxed)], check=True)
    muxed.replace(draft)


def thumbnails():
    OUT.mkdir(parents=True, exist_ok=True)
    scenes.thumbnail(OUT / "thumbnail_general.jpg", "cottage-depth.png", "STEVE IS IN VALHEIM", "real Minecraft, real Valheim, one world")
    scenes.thumbnail(OUT / "thumbnail_technical.jpg", "minecraft-inventory.png", "HOW I PUT MINECRAFT IN VALHEIM", "the technical breakdown")
    print("thumbnails ->", OUT)


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "stills"
    if what == "stills":
        for v in VIDEOS:
            build(v, only_stills=True)
        thumbnails()
    elif what == "all":
        thumbnails()
        for v in VIDEOS:
            build(v)
    elif what == "sound":
        # only the soundtrack, onto drafts already rendered: python video/build.py sound [general|technical]
        for v in (sys.argv[2:] or list(VIDEOS)):
            soundtrack(v, VIDEOS[v](), OUT / v, OUT / v / f"{v}_draft.mp4")
    else:
        build(what)
