#!/usr/bin/env bash
# Dedicated OpenRA server from a dev tree: binaries live in ./bin but mods live in ../mods.
# Default Game.Mod is ra — same as OpenRA.BrowserHost (WASM) unless you pass Game.Mod=… (use Game.Mod=cnc for Tiberian Dawn).
# Usage:
#   ./run-dedicated-server.sh
#   ./run-dedicated-server.sh Game.Mod=ra Server.Name="My server"
# Skip the desktop rebuild (not recommended if you also build Wasm): OPENRA_SKIP_DEDICATED_DESKTOP_BUILD=1 ./run-dedicated-server.sh
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

# Dedicated must load desktop OpenRA.Mods.Common (no OPENRA_BROWSER). A prior
# `dotnet build` with OpenRaBrowserBuild=true can leave browser-built DLLs in bin/
# and you get misleading "[browser] Failed to mount content..." from the server.
if [ "${OPENRA_SKIP_DEDICATED_DESKTOP_BUILD:-}" != "1" ]; then
	dotnet build "$REPO_ROOT/OpenRA.sln" -c Debug -p:OpenRaBrowserBuild=false -v q
fi

if [ ! -f ./bin/OpenRA.Server ]; then
	echo "run-dedicated-server: OpenRA.Server missing in ./bin (build OpenRA.sln)." >&2
	exit 1
fi

has_mod=false
for a in "$@"; do
	case "$a" in
		Game.Mod=*) has_mod=true ;;
	esac
done
if [ "$has_mod" = false ]; then
	set -- Game.Mod=ra "$@"
fi

exec "$REPO_ROOT/bin/OpenRA.Server" Engine.EngineDir=.. "$@"
