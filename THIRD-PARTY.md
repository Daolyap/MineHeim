# Third-party software

MineHeim's own code is MIT (see `LICENSE`). It uses, but does not contain, the following:

| what | license | how you get it |
|---|---|---|
| Fabric API 0.161.0+26.3 (FabricMC) | Apache-2.0 (its `LICENSE-fabric-api` is inside the jar) | **included** in the release zip, `minecraft\`, unmodified, as the official Maven artifact |
| Fabric Loader 0.19.5 (FabricMC) | Apache-2.0 | Prism Launcher downloads it for the MineHeim instance (or you install it with the official Fabric installer) |
| Prism Launcher 11.1.1 (PrismLauncher contributors) | GPL-3.0 | downloaded from its GitHub release by `Install.cmd` (portable build, hash-checked) into `%LOCALAPPDATA%\MineHeim\PrismLauncher`; not modified, not bundled |
| BepInEx 5 / BepInExPack_Valheim (BepInEx team, denikson) | LGPL-2.1 (BepInEx), MIT (HarmonyX, MonoMod) | downloaded from Thunderstore by `Install.cmd`, hash-checked |
| ReShade 6.8.0 add-on build (crosire) | BSD-3-Clause | downloaded from reshade.me by `Install.cmd`, hash-checked; the installer itself is never run |
| reshade-shaders `ReShade.fxh`, `ReShadeUI.fxh`, `DisplayDepth.fx` (crosire and contributors) | see crosire/reshade-shaders | downloaded from GitHub at a pinned commit, hash-checked |
| universal-modder (rehan-remade) | MIT | the design and the Minecraft-side approach this project started from |

Minecraft is © Mojang Studios / Microsoft and Valheim is © Iron Gate AB. This is a fan project: it contains no file, asset
or decompiled code of either game, and it needs your own copies of both.
