"""MineHeim's video soundtrack, synthesised from scratch (numpy only: no samples, no licences to track).

    sfx      whoosh, tick, thump, pop, chime, boom, riser: short one-shots placed on the timeline's cues
    music    a Nordic-flavoured score in D minor at 84 bpm: a drone pad, a frame drum, a plucked lyre (Karplus-Strong) and
             horn swells; `energy` (0..1 over time) decides which layers play, so quiet explainers and lively montages differ
    mix      music under everything, ducked where the voiceover speaks; sfx on top; a soft limiter; 48 kHz stereo WAV

    python video/audio.py demo      writes video/out/audio_demo.wav (every sound once, then 40 s of music rising and falling)
"""
from __future__ import annotations

import math
import sys
import wave
from pathlib import Path

import numpy as np

SR = 48000
OUT = Path(__file__).resolve().parent / "out"
RNG = np.random.default_rng(7)


# ------------------------------------------------------------------ building blocks

def t_axis(seconds: float) -> np.ndarray:
    return np.arange(int(seconds * SR)) / SR


def env_ad(n: int, attack: float, decay: float) -> np.ndarray:
    """Attack (s) then exponential decay with time constant `decay` (s)."""
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(t - attack, 0) / max(decay, 1e-4))


def lowpass(x: np.ndarray, cutoff) -> np.ndarray:
    """One-pole low-pass; cutoff in Hz, a number or an array as long as x (a sweep)."""
    c = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    a = 1 - np.exp(-2 * np.pi * c / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):  # short sounds only: fine in Python
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def lowpass_fast(x: np.ndarray, cutoff: float, passes: int = 2) -> np.ndarray:
    """A fixed one-pole low-pass for long signals (vectorised through scipy-free recursion in blocks)."""
    a = 1 - math.exp(-2 * math.pi * cutoff / SR)
    y = x.astype(float)
    for _ in range(passes):
        out = np.empty_like(y)
        acc = 0.0
        # recursion in chunks with numpy: y[n] = acc*(1-a)^k + a * sum ... (exact via cumulative products per chunk)
        k = 4096
        decay = (1 - a) ** np.arange(1, k + 1)
        for s in range(0, len(y), k):
            seg = y[s:s + k]
            m = len(seg)
            # out[n] = (1-a)^(n+1) * acc + a * sum_{j<=n} (1-a)^(n-j) seg[j]
            w = (1 - a) ** np.arange(m)
            conv = np.convolve(seg, w)[:m] * a
            out[s:s + m] = decay[:m] * acc + conv
            acc = out[s + m - 1]
        y = out
    return y


def highpass_fast(x: np.ndarray, cutoff: float) -> np.ndarray:
    return x - lowpass_fast(x, cutoff, passes=1)


def noise(n: int) -> np.ndarray:
    return RNG.standard_normal(n)


def norm(x: np.ndarray, peak: float = 0.9) -> np.ndarray:
    m = np.max(np.abs(x)) or 1.0
    return x / m * peak


# ------------------------------------------------------------------ sound effects (mono, -> stereo later)

def whoosh(seconds=0.7) -> np.ndarray:
    n = int(seconds * SR)
    sweep = 300 + 5200 * np.sin(np.linspace(0, np.pi, n)) ** 2
    x = lowpass(noise(n), sweep) * np.sin(np.linspace(0, np.pi, n)) ** 1.5
    return norm(x, 0.55)


def tick() -> np.ndarray:
    n = int(0.06 * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * 2400 * t) * env_ad(n, 0.0005, 0.012) + 0.3 * noise(n) * env_ad(n, 0.0002, 0.004)
    return norm(x, 0.4)


def thump() -> np.ndarray:
    """A block placed: a low body with a woody click (Minecraft-ish without being Minecraft's)."""
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    f = 95 * np.exp(-t * 9) + 55
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_ad(n, 0.002, 0.09)
    click = lowpass(noise(n), 2500) * env_ad(n, 0.0005, 0.012)
    return norm(body + 0.6 * click, 0.75)


def pop() -> np.ndarray:
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = 900 + 700 * np.exp(-t * 40)
    return norm(np.sin(2 * np.pi * np.cumsum(f) / SR) * env_ad(n, 0.001, 0.03), 0.4)


def chime(root=293.66) -> np.ndarray:
    """A bell for the logo: inharmonic partials, long ring."""
    n = int(3.5 * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for ratio, amp, dec in ((1.0, 1.0, 1.6), (2.0, 0.5, 1.1), (2.76, 0.35, 0.8), (5.4, 0.18, 0.4), (8.93, 0.1, 0.25)):
        x += amp * np.sin(2 * np.pi * root * ratio * t) * env_ad(n, 0.003, dec)
    return norm(x, 0.6)


def boom(seconds=2.6) -> np.ndarray:
    """An explosion: a sub drop and a noise blast that darkens as it dies."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    sub = np.sin(2 * np.pi * np.cumsum(70 * np.exp(-t * 3) + 28) / SR) * env_ad(n, 0.004, 0.55)
    blast = lowpass(noise(n), 4000 * np.exp(-t * 3.2) + 150) * env_ad(n, 0.002, 0.45)
    return norm(sub * 1.1 + blast, 0.95)


def riser(seconds=1.6) -> np.ndarray:
    n = int(seconds * SR)
    t = np.arange(n) / SR
    k = t / seconds
    tone = np.sin(2 * np.pi * np.cumsum(180 + 520 * k ** 2) / SR) * 0.25
    air = lowpass(noise(n), 400 + 7000 * k ** 2) * 0.8
    return norm((tone + air) * k ** 2, 0.5)


SFX = {"whoosh": whoosh, "tick": tick, "thump": thump, "pop": pop, "chime": chime, "boom": boom, "riser": riser}


# ------------------------------------------------------------------ music

BPM = 84
BEAT = 60 / BPM
D = 146.83  # D3


def note(semitones_from_d: int, octave: int = 0) -> float:
    return D * 2 ** (semitones_from_d / 12 + octave)


# a D minor (Aeolian) progression, two bars each: i - VI - III - VII (Dm - Bb - F - C)
CHORDS = [(0, 3, 7), (-4, 0, 3), (3, 7, 10), (-2, 2, 5)]


_pluck_cache: dict = {}


def pluck(freq: float, seconds: float, bright: float = 0.5) -> np.ndarray:
    """Karplus-Strong plucked string (a lyre); cached per note and brightness step (the loop is pure Python)."""
    key = (round(freq, 2), round(seconds, 2), round(bright, 1))
    if key not in _pluck_cache:
        _pluck_cache[key] = _pluck(freq, seconds, round(bright, 1))
    return _pluck_cache[key]


def _pluck(freq: float, seconds: float, bright: float) -> np.ndarray:
    n = int(seconds * SR)
    period = max(2, int(SR / freq))
    buf = RNG.uniform(-1, 1, period)
    out = np.empty(n)
    b = 0.5 + 0.49 * bright
    idx = 0
    for i in range(n):
        v = buf[idx]
        nxt = buf[(idx + 1) % period]
        buf[idx] = (b * v + (1 - b) * nxt) * 0.996
        out[i] = v
        idx = (idx + 1) % period
    return out * env_ad(n, 0.002, 1.2)


def frame_drum(accent: bool) -> np.ndarray:
    n = int(0.6 * SR)
    t = np.arange(n) / SR
    f = (62 if accent else 70) * (1 + 0.6 * np.exp(-t * 30))
    skin = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_ad(n, 0.002, 0.18)
    slap = lowpass(noise(n), 1800) * env_ad(n, 0.0005, 0.03)
    return (skin + 0.35 * slap) * (1.0 if accent else 0.6)


def pad_chord(chord, seconds: float) -> np.ndarray:
    """Detuned saw-ish voices, softened: the drone under everything."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for semi in chord:
        for det in (-0.12, 0.0, 0.11):
            f = note(semi, -1) * 2 ** (det / 12)
            # a few harmonics instead of a raw saw: warm, not buzzy
            for h, a in ((1, 1.0), (2, 0.45), (3, 0.22), (4, 0.1)):
                x += a * np.sin(2 * np.pi * f * h * t + RNG.uniform(0, 6.28))
    fade = np.minimum(1, np.minimum(t / 0.8, (seconds - t) / 0.8))
    return x * fade / 30


def horn(freq: float, seconds: float) -> np.ndarray:
    n = int(seconds * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5 * t)
    x = sum(a * np.sin(2 * np.pi * freq * h * np.cumsum(vib) / SR) for h, a in ((1, 1.0), (2, 0.6), (3, 0.35), (4, 0.18)))
    swell = np.sin(np.linspace(0, np.pi, n)) ** 1.5
    return x * swell / 3


def music(seconds: float, energy) -> np.ndarray:
    """The score for `seconds`; energy(t) in 0..1 picks the layers (pad always; lyre > 0.25; drum > 0.5; horns > 0.75)."""
    n = int(seconds * SR)
    mix = np.zeros((n, 2))
    bar = BEAT * 4
    bars = int(math.ceil(seconds / bar)) + 1

    def add(x, at, pan=0.0, gain=1.0):
        s = int(at * SR)
        if s >= n:
            return
        x = x[: n - s]
        l, r = math.cos((pan + 1) * math.pi / 4), math.sin((pan + 1) * math.pi / 4)
        mix[s:s + len(x), 0] += x * l * gain
        mix[s:s + len(x), 1] += x * r * gain

    for b in range(0, bars, 2):
        chord = CHORDS[(b // 2) % len(CHORDS)]
        at = b * bar
        e = energy(at)
        add(pad_chord(chord, bar * 2 + 0.8), at, pan=0.0, gain=0.55 + 0.25 * e)
        if e > 0.75:
            add(horn(note(chord[0], 0), bar * 2), at + bar * 0.5, pan=-0.3, gain=0.22)
        for beat in range(8):
            tb = at + beat * BEAT
            eb = energy(tb)
            if eb > 0.25:
                # lyre: an arpeggio through the chord, an octave up, eighth notes on strong energy
                steps = 2 if eb > 0.6 else 1
                for k in range(steps):
                    semi = chord[(beat * steps + k) % 3] + (12 if (beat // 2) % 2 else 0)
                    add(pluck(note(semi, 1), 1.4, bright=0.35 + 0.4 * eb), tb + k * BEAT / 2, pan=0.35 * (1 if k else -1),
                        gain=0.16 + 0.1 * eb)
            if eb > 0.5:
                add(frame_drum(beat % 4 == 0), tb, gain=0.5 + 0.3 * eb)
                if eb > 0.8 and beat % 2 == 1:
                    add(frame_drum(False), tb + BEAT / 2, gain=0.3)
    # a little space: a cheap stereo echo
    d = int(BEAT * 0.75 * SR)
    wet = np.zeros_like(mix)
    wet[d:, 0] = mix[:-d, 1] * 0.22
    wet[d:, 1] = mix[:-d, 0] * 0.22
    return mix + wet


# ------------------------------------------------------------------ mixing

def to_stereo(x: np.ndarray, pan=0.0) -> np.ndarray:
    l, r = math.cos((pan + 1) * math.pi / 4), math.sin((pan + 1) * math.pi / 4)
    return np.stack([x * l, x * r], axis=1)


def mix_track(seconds: float, energy, cues: list[tuple[float, str, float]], speech: list[tuple[float, float]],
              music_gain=0.32, duck_db=-9.0) -> np.ndarray:
    """Music (ducked during speech spans) + sound effects at their cues (time, name, gain)."""
    n = int(seconds * SR)
    out = music(seconds, energy) * music_gain
    # ducking envelope: smooth 0.25 s ramps around every speech span
    duck = np.ones(n)
    g = 10 ** (duck_db / 20)
    for a, b in speech:
        s, e = int(max(0, a - 0.2) * SR), int(min(seconds, b + 0.3) * SR)
        duck[s:e] = g
    duck = lowpass_fast(duck, 3.0)
    out *= duck[:, None]
    for at, name, gain in cues:
        x = SFX[name]()
        s = int(at * SR)
        if s >= n:
            continue
        x = to_stereo(x[: n - s] * gain)
        out[s:s + len(x)] += x
    # soft limiter
    return np.tanh(out * 1.1) * 0.89


def write_wav(path: Path, stereo: np.ndarray):
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = (np.clip(stereo, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


if __name__ == "__main__" and (len(sys.argv) < 2 or sys.argv[1] == "demo"):
    # every effect once (1.5 s apart), then 40 s of music rising from calm to full and back
    cues = [(0.5 + 1.5 * i, name, 0.9) for i, name in enumerate(SFX)]
    start = 0.5 + 1.5 * len(SFX) + 1.0
    total = start + 40.0

    def energy(t):
        if t < start:
            return 0.0
        k = (t - start) / 40.0
        return float(np.clip(math.sin(k * math.pi) * 1.05, 0, 1))

    track = mix_track(total, energy, cues, speech=[(start + 30, start + 36)])
    write_wav(OUT / "audio_demo.wav", track)
    print("wrote", OUT / "audio_demo.wav", f"{total:.0f} s")
