Baked FreeType fonts for the browser host (ORBF format)

Without .orbf files here, the engine uses SixLabors (ManagedTrueTypeFont). To match
desktop text metrics and rasterization, bake glyphs on a machine that has FreeType
(the same stack as OpenRA.Platforms.Default).

macOS Apple Silicon: the bundled NuGet freetype6.dylib is often x86_64 only. OpenRA.FontBake
will load Homebrew's arm64 library if present — run:  brew install freetype

1) Obtain the same .ttf files your mods reference (e.g. common|FreeSans.ttf).

2) From the repo root (shortcut — bakes default RA/CNC sizes and scales 1 and 2):

   ./bake-browser-fonts.sh /path/to/FreeSans.ttf /path/to/FreeSansBold.ttf

   Or manually after a Release build of OpenRA.FontBake:

   dotnet run --project OpenRA.FontBake/OpenRA.FontBake.csproj -c Release -- \
     --input /path/to/FreeSans.ttf \
     --output OpenRA.BrowserHost/wwwroot/engine/baked-fonts/ \
     --sizes 10,12,14,18,24,32 --scales 1,2

   Repeat for each face (e.g. FreeSansBold.ttf). The tool updates manifest.json.

3) Optional --codepoints: default is "extended" (tab, ASCII, U+00A0-U+017F).
   Example: --codepoints ascii

Optional MSBuild (run from repo root with a valid TTF path):

  dotnet build OpenRA.BrowserHost/OpenRA.BrowserHost.csproj -p:OpenRaBakeBrowserFonts=true \
    -p:OpenRaBakeFontInput=/path/to/FreeSans.ttf

This requires OpenRA.FontBake to be built first (same configuration).
