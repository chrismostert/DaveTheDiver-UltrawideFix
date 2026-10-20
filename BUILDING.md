# Building

## Requirements

- **.NET SDK 6.0 or newer** ([download](https://dotnet.microsoft.com/download)). The plugin targets `net6.0`, the runtime BepInEx 6 ships with; a newer SDK can build it.
- **Dave the Diver** (Steam, Windows).
- For the release zip: **bash** (Git Bash on Windows), `curl`, `unzip` and **Python 3**.

## 1. Prepare the game folder

The plugin compiles against assemblies that BepInEx generates from the game, so you need a game install with BepInEx:

1. Download the BepInEx build the release uses:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789](https://builds.bepinex.dev/projects/bepinex_be/789/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789%2B6cea286.zip)
   (or install a previous release zip of this mod, which bundles it).
2. Extract it into the game folder and **launch the game once**. On the first launch BepInEx generates `BepInEx/interop/`, which takes a few minutes.

Tip: work on a copy of the game folder instead of your Steam install. Add a `steam_appid.txt` containing `1868140` next to `DaveTheDiver.exe` so the copy starts directly (Steam must be running).

## 2. Build the plugin

```sh
dotnet build src/UltrawideFix.csproj -c Release -p:GameDir="C:\path\to\Dave the Diver"
```

`GameDir` defaults to `C:\Program Files (x86)\Steam\steamapps\common\Dave the Diver`.
The output is `src/bin/Release/net6.0/DaveTheDiverUltrawide.dll`. Copy it to `<game>/BepInEx/plugins/` to test.

### Diagnostics build

`src/Diag.cs` contains a developer helper that's only compiled in with `DIAG` defined:

```sh
dotnet build src/UltrawideFix.csproj -c Release -p:DefineConstants=DIAG -p:GameDir="..."
```

It turns on verbose logging and watches `<game>/BepInEx/cmd.txt`. Write a command into that file while the game runs and the output appears in `BepInEx/LogOutput.log`:

| Command | Output |
|---|---|
| `dump` | Screen, cameras, root canvases and scalers |
| `cams` | All cameras (including disabled) with viewport rects |
| `arf` | All `AspectRatioFitter`s |
| `tree <name> [depth]` | UI hierarchy with anchors, sizes and components |
| `at <x> <y>` | UI graphics under a screen point (pixels, origin bottom-left) |
| `img <path>` / `xf <path>` | Image details / transform chain of a GameObject |
| `invoke <path>` | Invoke a `Button`'s `onClick` (use `\|` for spaces in names) |
| `cust` | Sushi bar bubbles: where each should be on screen vs where it is (`dx` should be 0) |
| `wtv` | Number of `WorldToViewportPoint` calls the hook has seen, and how many it remapped |
| `goto <scene>` | Load a scene the way travel does (`DR_SushiBar`, `DR_Farm`, `DR_FishFarm`, `MermanVillage`, `Mini_BattleVIP`, `Mini_Momo`, `GlacialArea_Main_01`, `DR_Dredge`, `Boss_005_Gardon`, ...). No story checks; changes the save |
| `ui <dmg\|emoji\|talk\|guide\|sub> <vx> [vy]` | Show world UI (damage number, emoji, speech bubble, guide arrow, sub-helper effect) on a dummy target at viewport (vx, vy) of the full screen |
| `wui` / `wuiclear` | Alignment report of world UI positioned by the hooked methods: offset of each element from its projected target. Correct = the same offset at every screen position (and at 16:9) |
| `gfx <name>` | Clipping state (maskable, culled, RectMask2D) of every graphic under an object |
| `perf` | Average / max time per frame of each part of the plugin since the last `perf` (send it twice with a pause in between) |
| `findtest <Type>` | Cost of the `FindObjectsOfType` / `FindObjectsByType` variants for a type (all take milliseconds: they walk every object) |
| `bytes <rva> ...` | First bytes of `GameAssembly.dll` functions in memory (a hooked function starts with `FF-25`) |
| `res <w> <h> <mode>` | `Screen.SetResolution` |
| `tsl` / `tsl off` | Show / hide the Time Skip loading scene for the current area (visual only, doesn't skip time) |
| `mesh <path>` | Transform, mesh bounds, material/texture and the screen position of the mesh bounds corners (e.g. where a cloud card ends) |
| `xfset <px> <py> <pz> <sx> <sy> <sz> <path>` | Set an object's local position and scale (try a fix live) |
| `boats` / `boatin <n>` / `boatout` | NPC boats on the boat scene (character number, position, hidden) / sail boat `n` in like a story event (locks input until it arrives) / send it away. Visual only, no story progress |
| `show <0\|1> <path>` | `SetActive` on an object, also inactive ones below an active root |
| `cloudls` / `cloudput <folder>` | List the game's Steam Cloud save files / overwrite them with a folder's files (restoring a save backup; the game re-downloads its cloud saves on every start, so copying local files isn't enough) |
| `edges [minPx]` | World renderers that reach into the side areas outside the 16:9 strip, with their screen extent |
| `blur` | How the game's blur is wired: `KawaseBlur` renderer features, blur cameras, blur back panels |
| `blurtest <on\|off> [mask <ramp>\|nomask] [passes <n>] [down <n>]` | Toggle the blur camera of the active blur panel, optionally only outside the 16:9 strip (generated `_MaskTex`). Changes shared game materials until restart |
| `texdump <name>` | Save a loaded texture as `BepInEx/<name>.png` |
| `findperf [name]` | Time of one `GameObject.Find` and the number of active objects |

Don't ship a diagnostics build.

## 3. Package a release

```sh
GAME_DIR="/c/path/to/Dave the Diver" ./package.sh
```

This builds the plugin, downloads the pinned BepInEx build to `.cache/` (or uses `BEPINEX_ZIP` if set), and creates:

- `dist/DaveTheDiver-UltrawideFix-vX.Y.Z/`: the files players copy into their game folder
- `dist/DaveTheDiver-UltrawideFix-vX.Y.Z.zip`: attach this to the GitHub release

The zip contains BepInEx, the plugin, `package/README.txt` (player instructions) and `package/BepInEx/config/BepInEx.cfg` (turns the BepInEx console window off). `dist/` and `.cache/` are git-ignored.

Bump `Version` in `src/Plugin.cs` before packaging a new release.

## Notes

- BepInEx 6 is still pre-release; only bleeding-edge builds support Unity 6 (the game uses 6000.0.52f1). When updating `BEPINEX_BUILD` in `package.sh`, test a clean install, since the interop assemblies change.
- After a game update BepInEx regenerates the interop assemblies on the next launch. If the game renames members the plugin uses (`CameraResolution.m_CameraViewRect`, `m_LeftLetterBoxModifier`, …), the build fails and the plugin needs updating.
- Il2CppDumper/script.json lists overloads separately, but to tell which native function is which overload, look at a call site: IL2CPP passes a hidden `MethodInfo*` as the last argument, so `WorldToViewportPoint(Vector3)` is called with 4 register arguments, not the `eye` overload. Check with the `bytes` diagnostics command that the function the game actually calls got hooked.
- Don't hook `CameraResolution.SetResolution`, `UpdateWideResolution` or `UpdateAutoResolution` with Harmony. IL2CPP compiled `UpdateWideResolution` and `UpdateAutoResolution` into the same native function, and hooking them crashed the game or threw exceptions. The plugin works around them in `LateUpdate` instead.
