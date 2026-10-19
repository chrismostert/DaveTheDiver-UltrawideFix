#!/bin/bash
# Builds the plugin and assembles the release zip in dist/.
#
#   GAME_DIR=/c/path/to/Dave\ the\ Diver ./package.sh
#
# Environment variables:
#   GAME_DIR     Game folder with BepInEx installed and launched once (needed to compile).
#                Default: the standard Steam install location.
#   DOTNET       dotnet executable (default: dotnet on PATH).
#   BEPINEX_ZIP  BepInEx zip to bundle. Default: the pinned build below, downloaded to .cache/.
# Needs bash (Git Bash on Windows), curl, unzip and python.
set -euo pipefail

BEPINEX_BUILD=789
BEPINEX_FILE="BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789%2B6cea286.zip"

HERE="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${GAME_DIR:-/c/Program Files (x86)/Steam/steamapps/common/Dave the Diver}"
DOTNET="${DOTNET:-dotnet}"
VERSION="$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$HERE/src/Plugin.cs")"
NAME="DaveTheDiver-UltrawideFix-v$VERSION"
OUT="$HERE/dist/$NAME"

if [ -z "${BEPINEX_ZIP:-}" ]; then
  BEPINEX_ZIP="$HERE/.cache/bepinex-be.$BEPINEX_BUILD.zip"
  if [ ! -f "$BEPINEX_ZIP" ]; then
    mkdir -p "$HERE/.cache"
    echo "Downloading BepInEx build $BEPINEX_BUILD..."
    curl -fL -o "$BEPINEX_ZIP" "https://builds.bepinex.dev/projects/bepinex_be/$BEPINEX_BUILD/$BEPINEX_FILE"
  fi
fi

GAME_DIR_WIN="$GAME_DIR"
command -v cygpath >/dev/null && GAME_DIR_WIN="$(cygpath -w "$GAME_DIR")"
"$DOTNET" build "$HERE/src/UltrawideFix.csproj" -c Release "-p:GameDir=$GAME_DIR_WIN"

rm -rf "$OUT" "$OUT.zip"
mkdir -p "$OUT"
unzip -q "$BEPINEX_ZIP" -d "$OUT"
rm -f "$OUT/changelog.txt"
cp -r "$HERE/package/." "$OUT/"
mkdir -p "$OUT/BepInEx/plugins"
cp "$HERE/src/bin/Release/net6.0/DaveTheDiverUltrawide.dll" "$OUT/BepInEx/plugins/"

# Python's zipfile writes portable forward-slash paths (PowerShell 5's Compress-Archive doesn't).
python -c "
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, 'w', zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(src):
        dirs.sort()
        for f in sorted(files):
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, src).replace(os.sep, '/'))
" "$OUT" "$OUT.zip"
echo "Created dist/$NAME.zip"
