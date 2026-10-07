"""Fetch a portable MSVC x64 toolchain + Windows SDK from Microsoft's own Visual Studio release manifest.
No installer, no admin rights, nothing system-wide: everything is unpacked into one folder.

    python tools/get_msvc.py [dest]        # default C:\\Users\\<you>\\tools\\msvc

Only URLs listed in Microsoft's manifest (download.visualstudio.microsoft.com) are used, each payload is checked
against the SHA-256 the manifest gives, and MSIs are unpacked with `msiexec /a` (an administrative extract, which
installs nothing). Afterwards `tools/msvc_env.ps1` sets INCLUDE / LIB / PATH for a build shell.

This is the same idea as mmozeiko's portable-msvc.py (https://gist.github.com/mmozeiko/7f3162ec2988e81e56d5c4e22cde9977),
written fresh here so every URL and hash check is visible in this repo.
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

CHANNEL = "https://aka.ms/vs/17/release/channel"
SDK_ID = "Win11SDK_10.0.26100"
SDK_MSIS = [
    "Windows SDK Desktop Headers x64-x86_en-us.msi",
    "Windows SDK Desktop Headers x86-x86_en-us.msi",
    "Windows SDK Desktop Libs x64-x86_en-us.msi",
    "Windows SDK OnecoreUap Headers x64-x86_en-us.msi",
    "Windows SDK OnecoreUap Headers x86-x86_en-us.msi",
    "Windows SDK for Windows Store Apps Headers-x86_en-us.msi",
    "Windows SDK for Windows Store Apps Headers OnecoreUap-x86_en-us.msi",
    "Windows SDK for Windows Store Apps Tools-x86_en-us.msi",
    "Windows SDK for Windows Store Apps Libs-x86_en-us.msi",
    "Universal CRT Headers Libraries and Sources-x86_en-us.msi",
]


def get(url, dest=None):
    req = urllib.request.Request(url, headers={"User-Agent": "mineheim-get-msvc/0.1"})
    with urllib.request.urlopen(req, timeout=120) as r:
        data = r.read()
    if dest is not None:
        Path(dest).parent.mkdir(parents=True, exist_ok=True)
        Path(dest).write_bytes(data)
    return data


def fetch(payload, cache):
    name = payload["fileName"].replace("\\", "/").split("/")[-1]
    path = cache / name
    want = payload.get("sha256", "").lower()
    if path.exists() and (not want or hashlib.sha256(path.read_bytes()).hexdigest() == want):
        return path
    print(f"  downloading {name} ({payload['size'] / 1e6:.1f} MB)")
    data = get(payload["url"])
    if want and hashlib.sha256(data).hexdigest() != want:
        raise SystemExit(f"hash mismatch for {name}: refusing to use it")
    path.write_bytes(data)
    return path


def main(root):
    root = Path(root)
    cache = root / "_download"
    cache.mkdir(parents=True, exist_ok=True)
    channel = json.loads(get(CHANNEL).decode("utf-8-sig"))
    item = next(i for i in channel["channelItems"] if i["id"] == "Microsoft.VisualStudio.Manifests.VisualStudio")
    vs = json.loads(get(item["payloads"][0]["url"]).decode("utf-8-sig"))
    by_id = {}
    for p in vs["packages"]:
        by_id.setdefault(p["id"].lower(), []).append(p)

    if (root / "VC" / "Tools" / "MSVC").exists():
        print("MSVC already unpacked, skipping")
    else:
        # newest MSVC toolset
        rx = re.compile(r"^microsoft\.vc\.(\d+)\.(\d+)\.(\d+)\.(\d+)\.tools\.hostx64\.targetx64\.base$")
        versions = sorted((tuple(map(int, m.groups())), m.group(0)) for m in (rx.match(k) for k in by_id) if m)
        ver = versions[-1][0]
        prefix = "microsoft.vc.%d.%d.%d.%d" % ver
        print("MSVC package set:", prefix)
        for suffix in ("tools.hostx64.targetx64.base", "tools.hostx64.targetx64.res.base", "crt.headers.base",
                       "crt.x64.desktop.base", "crt.x64.store.base"):
            for pkg in by_id.get(f"{prefix}.{suffix}", []):
                if pkg.get("language", "neutral") not in ("neutral", "en-US"):
                    continue
                for payload in pkg["payloads"]:
                    if not payload["fileName"].lower().endswith(".vsix"):
                        continue
                    vsix = fetch(payload, cache)
                    with zipfile.ZipFile(vsix) as z:
                        for member in z.namelist():
                            if member.startswith("Contents/") and not member.endswith("/"):
                                target = root / member[len("Contents/"):]
                                target.parent.mkdir(parents=True, exist_ok=True)
                                with z.open(member) as src, open(target, "wb") as dst:
                                    shutil.copyfileobj(src, dst)


    # Windows SDK: MSIs (plus the cabs each one names), unpacked with an administrative extract
    sdk = by_id[SDK_ID.lower()][0]
    payloads = {p["fileName"].replace("\\", "/").split("/")[-1]: p for p in sdk["payloads"]}
    for msi_name in SDK_MSIS:
        msi = fetch(payloads[msi_name], cache)
        for cab in set(re.findall(rb"[0-9a-f]{32}\.cab", msi.read_bytes())):
            cab_name = cab.decode()
            if cab_name in payloads:
                fetch(payloads[cab_name], cache)
        subprocess.run(["msiexec.exe", "/a", str(msi), "/quiet", "/qn", f"TARGETDIR={root / '_sdk'}"], check=True)

    sdk_root = root / "_sdk" / "Windows Kits" / "10"
    sdk_ver = sorted(p.name for p in (sdk_root / "Include").iterdir())[-1]
    msvc_ver = sorted(p.name for p in (root / "VC" / "Tools" / "MSVC").iterdir())[-1]
    (root / "msvc-paths.json").write_text(json.dumps({
        "msvc": msvc_ver, "sdk": sdk_ver, "sdk_root": str(sdk_root), "root": str(root)}, indent=2))
    print(f"done: MSVC {msvc_ver}, Windows SDK {sdk_ver} in {root}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else str(Path.home() / "tools" / "msvc"))
