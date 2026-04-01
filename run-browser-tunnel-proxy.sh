#!/usr/bin/env bash
# WebSocket → TCP tunnel for browser OpenRA client. Defaults match docs/browser-multiplayer/04.
# Env: LISTEN (default http://127.0.0.1:8787), TARGET_HOST, TARGET_PORT (dedicated TCP).
set -euo pipefail
cd "$(dirname "$0")"
export LISTEN="${LISTEN:-http://127.0.0.1:8787}"
export TARGET_HOST="${TARGET_HOST:-127.0.0.1}"
export TARGET_PORT="${TARGET_PORT:-1234}"
dotnet run \
	--project tools/OpenRA.BrowserTunnelProxy/OpenRA.BrowserTunnelProxy.csproj \
	-c Release \
	"$@"
