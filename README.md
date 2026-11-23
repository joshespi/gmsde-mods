# GMS:DE plugins: FOV Unlock + Borderless Window

Source code for small quality-of-life mods for Gold Mining Simulator: Definitive Edition (GMS:DE), a Unity game on Steam. Each mod is a standalone BepInEx plugin with its own folder and project. This repo holds the source and build instructions; the compiled mods are published on Nexus Mods.

- FOV Unlock: https://www.nexusmods.com/goldminingsimulatordefinitiveedition/mods/28
- Borderless Window: https://www.nexusmods.com/goldminingsimulatordefinitiveedition/mods/29
- Release zips: `release/`

BepInEx 5.4.23.3 (x64, Mono) plugin. Game is Unity 2022.3.62f3.

## FOV Unlock (`fov-unlock/`)

### How the cap works
- `QualitySettingsLoader.SetFOV(int)`: fov = 60 + 5 * index
- Menu FOV control is a `MenuNavigationBar` with 7 options (60..90). That list is the cap; there's no clamp in code.
- `CameraSettingsGetter.SetFov(float)` writes `camera.fieldOfView`.

### Patches
- `MainMenuGraphicsSimple.Awake` postfix: rebuilds FOV options 60..MaxFov.
- `CameraSettingsGetter.SetFov` prefix: forces `FovOverride` when > 0.

### Config (`BepInEx/config/espi.gmsde.fovunlock.cfg`, created on first launch)
- `MaxFov` (90-150, default 120): menu max.
- `FovOverride` (0-170, default 0): exact FOV, ignores menu. Live-reloads.

## Borderless Window (`borderless/`)
Win32 P/Invoke on the `UnityWndClass` window: strips caption/frame, sets size + position. Only acts in the game's Window mode.
Rechecks every 0.5s and reapplies if the game/Unity puts the frame back (resolution or mode change). Config size wins over the in-game resolution.

Config (`BepInEx/config/espi.gmsde.borderless.cfg`), all live-reload:
- `Enabled` (true), `Width` (0), `Height` (0). Width/Height 0 = keep game resolution.
- `Center` (true): centers on the current monitor. `X`/`Y` (0) used when false.
- `ToggleKey` (F10)

## Build (Docker)
Run from the repository root:

```bash
docker run --rm -v "$PWD:/src" -v "<game>/GoldMiningSimulatorDefinitiveEdition_Data/Managed:/managed:ro" \
  -v "<game>/BepInEx/core:/bie:ro" mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -c 'mkdir -p /b && cp /src/Directory.Build.props /b/ && cp -r /src/fov-unlock /b/ && dotnet build /b/fov-unlock/FovUnlock.csproj -c Release -p:ManagedDir=/managed -p:BepInExCore=/bie -o /src/fov-unlock/out'
```

For Borderless Window, replace `fov-unlock` with `borderless` and `FovUnlock.csproj` with `BorderlessWindow.csproj`.

On Windows with the SDK installed, `dotnet build -c Release` in `fov-unlock/` or `borderless/` uses the default Steam path. For another installation, add `-p:GameDir="C:\path\to\game"`.

## Uninstall
Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `BepInEx/` from the game folder.
Saved FOV > 90 still applies without the mod (no clamp on load); reopening the menu snaps it back to 90.
