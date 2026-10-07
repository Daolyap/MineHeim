"""Fetch what the compositor needs into third_party/ (none of it is redistributed from this repo):

  third_party/reshade/            ReShade 6.8.0 add-on API headers (crosire/reshade, tag v6.8.0)
  third_party/reshade-runtime/    ReShade64.dll 6.8.0 with add-on support, taken out of the official installer
                                  ReShade_Setup_6.8.0_Addon.exe (reshade.me). The installer is never run: it carries
                                  its DLLs as a zip appended to the exe.
  third_party/shaders/            ReShade.fxh, ReShadeUI.fxh, DisplayDepth.fx (crosire/reshade-shaders, slim branch)

    python compositor/fetch_deps.py
"""
import hashlib
import sys
import urllib.request
import zipfile
from pathlib import Path

RESHADE = "6.8.0"
HEADERS = ["reshade.hpp", "reshade_api.hpp", "reshade_api_device.hpp", "reshade_api_pipeline.hpp",
           "reshade_api_resource.hpp", "reshade_api_format.hpp", "reshade_events.hpp", "reshade_overlay.hpp"]
SHADERS = ["ReShade.fxh", "ReShadeUI.fxh", "DisplayDepth.fx", "DisplayDepth_L10N.fxh"]
UA = {"User-Agent": "mineheim-fetch/0.1"}


def get(url, referer=None):
    req = urllib.request.Request(url, headers={**UA, **({"Referer": referer} if referer else {})})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def main():
    root = Path(__file__).resolve().parent.parent / "third_party"
    (root / "reshade").mkdir(parents=True, exist_ok=True)
    (root / "reshade-runtime").mkdir(parents=True, exist_ok=True)
    (root / "shaders").mkdir(parents=True, exist_ok=True)

    for f in HEADERS:
        (root / "reshade" / f).write_bytes(get(f"https://raw.githubusercontent.com/crosire/reshade/v{RESHADE}/include/{f}"))
    for f in SHADERS:
        (root / "shaders" / f).write_bytes(get(f"https://raw.githubusercontent.com/crosire/reshade-shaders/slim/Shaders/{f}"))

    exe = root / "reshade-runtime" / f"ReShade_Setup_{RESHADE}_Addon.exe"
    if not exe.exists():
        exe.write_bytes(get(f"https://reshade.me/downloads/ReShade_Setup_{RESHADE}_Addon.exe", referer="https://reshade.me/"))
    print("installer sha256", hashlib.sha256(exe.read_bytes()).hexdigest(), f"({exe.stat().st_size} bytes)")
    with zipfile.ZipFile(exe) as z:
        names = z.namelist()
        print("zip members:", names)
        (root / "reshade-runtime" / "ReShade64.dll").write_bytes(z.read("ReShade64.dll"))
    print("ReShade64.dll sha256", hashlib.sha256((root / "reshade-runtime" / "ReShade64.dll").read_bytes()).hexdigest())
    print("done:", root)


if __name__ == "__main__":
    sys.exit(main())
