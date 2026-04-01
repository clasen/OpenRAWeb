#!/usr/bin/env bash
# OpenRA Blazor WebAssembly host (experimental).
# HTTP Cache-Control hints for production: OpenRA.BrowserHost/HTTP-CACHE-NOTES.txt
#
# Listens on 127.0.0.1:5284 by default. If that port is already in use (e.g. another
# host still running), the next free port is chosen. Override with BROWSER_HOST_PORT.
# Build configuration can be overridden with BROWSER_HOST_CONFIGURATION (Debug/Release).
#
# Ensures freeware game assets exist under wwwroot/support (see ensure-browser-game-content.sh).
#   BROWSER_ENSURE_MOD   ra (default), cnc, or all
#   SKIP_BROWSER_GAME_CONTENT=1  skip fetch (offline / CI)
set -euo pipefail
cd "$(dirname "$0")"

# Freeware assets for Wasm (gitignored under wwwroot/support). Fetch if missing.
./ensure-browser-game-content.sh "${BROWSER_ENSURE_MOD:-ra}"

pick_port() {
	local start="${BROWSER_HOST_PORT:-5284}"
	local max=$((start + 128))
	local p=$start
	while [ "$p" -le "$max" ]; do
		if ! port_is_listening "$p"; then
			echo "$p"
			return 0
		fi
		p=$((p + 1))
	done
	echo "run-browser-host: no free TCP port between $start and $max" >&2
	return 1
}

port_is_listening() {
	local p=$1
	if command -v lsof >/dev/null 2>&1; then
		lsof -nP -iTCP:"$p" -sTCP:LISTEN >/dev/null 2>&1
	else
		# Cannot probe; try anyway (may fail at bind time).
		return 1
	fi
}

# Restore with browser define so referenced mods (e.g. net9 assets) match the WASM build graph.
dotnet restore OpenRA.BrowserHost/OpenRA.BrowserHost.csproj -p:OpenRaBrowserBuild=true

PORT="$(pick_port)"
URL="http://127.0.0.1:${PORT}"
CONFIGURATION="${BROWSER_HOST_CONFIGURATION:-Debug}"
echo "Browser host → ${URL}/"
# --urls overrides launchSettings applicationUrl (avoids fixed 5284 when already taken).
dotnet run \
	--project OpenRA.BrowserHost/OpenRA.BrowserHost.csproj \
	-c "$CONFIGURATION" \
	-p:OpenRaBrowserBuild=true \
	--no-restore \
	--urls "$URL" \
	"$@"
