"""Switch the INSTALLED MineHeim.fx between the normal look and a neutral one (no relighting, colour grade, haze,
glow, contact shadows or edge softening) so Minecraft's colours reach the picture unchanged: the oracles detect the
gold pillar by colour. The add-on hot-reloads the file within a second.

    python compositor/neutral_look.py on|off [ValheimDir]
"""
import re
import sys
from pathlib import Path

NEUTRAL = {"LightMatch": "0.0", "GradeMatch": "0.0", "HazeStrength": "0.0", "ContactShadow": "0.0", "BloomStrength": "0.0", "EdgeSoftness": "0.0"}


def main():
    mode = sys.argv[1]
    vd = Path(sys.argv[2] if len(sys.argv) > 2 else r"C:\Program Files (x86)\Steam\steamapps\common\Valheim")
    dst = vd / "reshade-shaders" / "Shaders" / "MineHeim.fx"
    if mode == "off":
        dst.write_text((Path(__file__).parent / "MineHeim.fx").read_text(encoding="utf-8"), encoding="utf-8")
        print("normal look restored")
        return
    s = (Path(__file__).parent / "MineHeim.fx").read_text(encoding="utf-8")
    for name, value in NEUTRAL.items():
        s, n = re.subn(r"(uniform float " + name + r"\b.*?> = )[0-9.]+;", r"\g<1>" + value + ";", s, count=1, flags=re.S)
        assert n == 1, name
    s, n = re.subn(r"(uniform bool DebugCode.*?= )false;", r"\g<1>true;", s, count=1)
    assert n == 1, "DebugCode"
    dst.write_text(s, encoding="utf-8")
    print("neutral look on:", ", ".join(NEUTRAL))


main()
