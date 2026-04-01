#!/usr/bin/env bash
# Prints the browser URL with ?tunnel= for local join (defaults: host 5284, tunnel ws 8787).
# Usage:
#   ./print-browser-multiplayer-join-url.sh 'http://127.0.0.1:5285/'
# Env: HOST_HTTP, TUNNEL_WS (override defaults if no argument).
set -euo pipefail
HOST_HTTP="${1:-${HOST_HTTP:-http://127.0.0.1:5284/}}"
TUNNEL_WS="${TUNNEL_WS:-ws://127.0.0.1:8787/}"
base="${HOST_HTTP%/}"
echo "${base}/?tunnel=${TUNNEL_WS}"
