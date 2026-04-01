#!/usr/bin/env bash
# Bake desktop FreeType glyphs into OpenRA.BrowserHost/wwwroot/engine/baked-fonts/
# Usage: ./bake-browser-fonts.sh /path/to/FreeSans.ttf [/path/to/FreeSansBold.ttf ...]
#
# macOS Apple Silicon: NuGet freetype6 is often x86_64 only; install arm64 FreeType:
#   brew install freetype
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/OpenRA.BrowserHost/wwwroot/engine/baked-fonts"
mkdir -p "$OUT"
if [[ $# -lt 1 ]]; then
  echo "Usage: $0 /path/to/font.ttf [more.ttf ...]" >&2
  exit 1
fi
for f in "$@"; do
  if [[ ! -f "$f" ]]; then
    echo "Error: file not found: $f" >&2
    echo "" >&2
    echo "Use the real path to each .ttf on your machine (not a placeholder like /ruta/...)." >&2
    echo "This repo often does not ship mods/common fonts; copy them from an OpenRA install, e.g.:" >&2
    echo "  macOS app bundle: /Applications/OpenRA.app/Contents/Resources/mods/common/FreeSans.ttf" >&2
    echo "  Or from the same folder where you run the desktop game (mods/common/*.ttf)." >&2
    exit 1
  fi
  dotnet run --project "$ROOT/OpenRA.FontBake/OpenRA.FontBake.csproj" -c Release -- \
    --input "$f" --output "$OUT" --sizes "10,12,14,18,24,32" --scales "1,2"
done
