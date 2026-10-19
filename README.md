# Dave the Diver – Ultrawide Fix

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin that lets Dave the Diver use the full width of ultrawide monitors (21:9, 32:9, …) instead of showing black bars at the sides.

- The game world (diving, boat, sushi bar, cutscenes, title screen) fills the whole screen.
- HUD and menus stay in a centered 16:9 area, the way the game was designed.
- Screen fades, dimmed popup backgrounds, cutscene bars and vignettes are extended to the full width.
- At 16:9 or narrower the game behaves exactly as without the fix.

Tested with the Steam version v1.0.6.2113 at 3440×1440.

## Install

1. Download `DaveTheDiver-UltrawideFix-vX.Y.Z.zip` from the [Releases](../../releases) page.
2. In Steam, right-click Dave the Diver → **Manage → Browse local files**.
3. Extract **everything** from the zip into that folder (next to `DaveTheDiver.exe`).
4. Start the game. **The first launch takes 1–3 minutes** before the window appears; later launches are normal.
5. In the game: **Settings → Graphics**, pick your monitor's resolution and *Full Window Mode* or *Fullscreen*.

Steam Deck / Linux (Proton): add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the launch options.

## Uninstall

Delete `BepInEx/`, `dotnet/`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from the game folder.

## Configuration

After the first launch, edit `BepInEx/config/dtd.ultrawide.cfg`:

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Turn the whole fix on/off. |
| `WidenOverlays` | `true` | Extend fades and popup backgrounds to the full width. |
| `ClipUIToCenter` | `true` | Hide UI the game keeps just outside the 16:9 area (e.g. slide-in tabs). |
| `CoverImages` | `Title,DummySushiBar` | Full-screen background images that are zoomed to fill the width. |
| `VerboseLogging` | `false` | Log every change the fix makes to `BepInEx/LogOutput.log`. |

## How it works

- **Camera:** the game's `CameraResolution` computes a centered 16:9 viewport for wider screens and applies it to every camera. The plugin replaces that viewport with the full screen and hides the black side masks.
- **UI:** every UI canvas puts its content in a panel with a 16:9 `AspectRatioFitter`, so the HUD stays centered by itself. When a solid-color, full-width image inside such a panel is enabled (hooked through `Image.OnEnable`), the plugin adds two side strips that extend it to the screen edges and mirror its color, alpha and visibility. Vignettes are stretched, the listed background images are scaled up, and a `RectMask2D` on each panel keeps parked off-screen UI hidden.

The game's own layout is never modified for the HUD, so menus and navigation behave as on a 16:9 screen.

## Building

See [BUILDING.md](BUILDING.md).

## License

The plugin source is released under the [MIT License](LICENSE). The release zip bundles [BepInEx 6](https://github.com/BepInEx/BepInEx), which is licensed under LGPL-2.1.

Not affiliated with MINTROCKET or NEXON.
