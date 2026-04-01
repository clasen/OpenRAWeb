#!/usr/bin/env bash
# One-command local browser multiplayer stack:
# - Incremental build (desktop server + browser host + tunnel proxy)
# - Dedicated server
# - Browser WebSocket tunnel proxy
# - Browser host
#
# Runs everything in one terminal and tears down child processes on Ctrl+C.
#
# Usage:
#   ./run-browser-multiplayer.sh
#   ./run-browser-multiplayer.sh --mod cnc
#   ./run-browser-multiplayer.sh --no-build -- Game.Mod=ra Server.Name="Local test"
#   ./run-browser-multiplayer.sh --open
set -euo pipefail
# Background jobs get their own process groups so Ctrl+C can kill the whole tree (dotnet, etc.).
set -m

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

MOD="ra"
DO_BUILD=1
OPEN_BROWSER=0
DEDICATED_ARGS=()

usage() {
	cat <<'EOF'
run-browser-multiplayer.sh

Starts dedicated server + browser tunnel + browser host in one terminal.

Options:
  --mod <id>      Dedicated mod (default: ra)
  --no-build      Skip pre-build step
  --open          Open the join URL in the default browser (macOS: open)
  --help          Show this help
  --              Pass remaining args to run-dedicated-server.sh

Environment:
  BROWSER_HOST_CONFIGURATION  Debug (default for orchestrator) or Release.
                              Release can fail to build WASM JS interop in some setups.
  SKIP_BROWSER_GAME_CONTENT   If 1, do not auto-fetch wwwroot/support assets (ensure-browser-game-content.sh).

Examples:
  ./run-browser-multiplayer.sh
  ./run-browser-multiplayer.sh --open
  ./run-browser-multiplayer.sh --mod cnc
  ./run-browser-multiplayer.sh --no-build -- Server.Name="My Local Server"
EOF
}

while (($#)); do
	case "$1" in
		--mod)
			if [ $# -lt 2 ]; then
				echo "--mod requires a value" >&2
				exit 1
			fi
			MOD="$2"
			shift 2
			;;
		--no-build)
			DO_BUILD=0
			shift
			;;
		--open)
			OPEN_BROWSER=1
			shift
			;;
		--help|-h)
			usage
			exit 0
			;;
		--)
			shift
			DEDICATED_ARGS+=("$@")
			break
			;;
		*)
			echo "Unknown option: $1" >&2
			usage >&2
			exit 1
			;;
	esac
done

case "$MOD" in
	ra|cnc)
		./ensure-browser-game-content.sh "$MOD"
		;;
	*)
		echo "== Note: browser content auto-fetch only for mod ra|cnc (mod=$MOD) ==" >&2
		;;
esac

port_is_listening() {
	local p=$1
	if command -v lsof >/dev/null 2>&1; then
		lsof -nP -iTCP:"$p" -sTCP:LISTEN >/dev/null 2>&1
	else
		return 1
	fi
}

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
	echo "run-browser-multiplayer: no free TCP port between $start and $max" >&2
	return 1
}

# Wait until something listens on TCP port or subshell PID exits (build/runtime failure).
wait_for_port_or_fail() {
	local port=$1
	local watch_pid=$2
	local max_wait="${3:-90}"
	local i=0
	while [ "$i" -lt "$max_wait" ]; do
		if ! kill -0 "$watch_pid" 2>/dev/null; then
			echo "run-browser-multiplayer: browser host process exited before port $port was ready (see [host] lines above for build errors)." >&2
			return 1
		fi
		if port_is_listening "$port"; then
			return 0
		fi
		sleep 1
		i=$((i + 1))
	done
	echo "run-browser-multiplayer: timed out waiting for http://127.0.0.1:${port}/ (still not listening)." >&2
	return 1
}

open_join_url() {
	local url=$1
	case "$(uname -s)" in
		Darwin) open "$url" ;;
		MINGW*|MSYS*|CYGWIN*) cmd.exe /c start "" "$url" 2>/dev/null || true ;;
		*) command -v xdg-open >/dev/null 2>&1 && xdg-open "$url" || echo "Open manually: $url" ;;
	esac
}

to_ws_url() {
	local listen_url="$1"
	local ws_url
	ws_url="$(python3 - "$listen_url" <<'PY'
import sys
from urllib.parse import urlparse
url = sys.argv[1]
u = urlparse(url)
if u.scheme not in ("http", "https"):
    raise SystemExit(1)
scheme = "wss" if u.scheme == "https" else "ws"
path = u.path or "/"
if not path.startswith("/"):
    path = "/" + path
if path == "":
    path = "/"
print(f"{scheme}://{u.netloc}{path}")
PY
)"
	echo "$ws_url"
}

if [ "$DO_BUILD" -eq 1 ]; then
	echo "== Building browser multiplayer stack (incremental) =="
	./build-browser-multiplayer-local.sh
else
	echo "== Skipping build (--no-build) =="
fi

LISTEN_URL="${LISTEN:-http://127.0.0.1:8787}"
TARGET_HOST="${TARGET_HOST:-127.0.0.1}"
TARGET_PORT="${TARGET_PORT:-1234}"
HOST_PORT="$(pick_port)"
HOST_URL="http://127.0.0.1:${HOST_PORT}/"
TUNNEL_WS="${TUNNEL_WS:-$(to_ws_url "$LISTEN_URL")}"

echo
echo "== Launch configuration =="
echo "mod         : $MOD"
echo "dedicated   : ${TARGET_HOST}:${TARGET_PORT}"
echo "tunnel      : $LISTEN_URL  (ws: $TUNNEL_WS)"
echo "browser host: $HOST_URL"
echo

pids=()

# Leader PID equals PGID when job control is on (set -m). Negative PID = signal whole group.
kill_proc_group() {
	local pid=$1
	[[ -z "$pid" ]] && return 0
	kill -0 "$pid" 2>/dev/null || return 0
	kill -TERM "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
	local w=0
	while kill -0 "$pid" 2>/dev/null && [[ $w -lt 20 ]]; do
		sleep 0.1
		w=$((w + 1))
	done
	if kill -0 "$pid" 2>/dev/null; then
		kill -KILL "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
	fi
}

start_bg() {
	local name="$1"
	shift

	if command -v stdbuf >/dev/null 2>&1; then
		(
			"$@" \
				> >(stdbuf -oL sed "s/^/[${name}] /") \
				2> >(stdbuf -oL sed "s/^/[${name}] /" >&2)
		) &
	else
		# macOS often has no stdbuf; prefix lines without line-buffering tweaks.
		(
			"$@" 2>&1 | sed "s/^/[${name}] /"
		) &
	fi
	pids+=("$!")
}

cleanup() {
	local code=$?
	trap - EXIT INT TERM

	echo
	echo "== Stopping browser multiplayer stack =="
	for pid in "${pids[@]:-}"; do
		kill_proc_group "$pid"
	done

	for pid in "${pids[@]:-}"; do
		wait "$pid" 2>/dev/null || true
	done

	exit "$code"
}

trap cleanup EXIT INT TERM

dedicated_cmd=(./run-dedicated-server.sh "Game.Mod=${MOD}")
if [ "${#DEDICATED_ARGS[@]}" -gt 0 ]; then
	dedicated_cmd+=("${DEDICATED_ARGS[@]}")
fi

# Do not skip dedicated desktop rebuild here: browser host builds can leave browser
# variants in ./bin, and dedicated must enforce desktop assemblies before launch.
start_bg "dedicated" "${dedicated_cmd[@]}"
start_bg "tunnel" env LISTEN="$LISTEN_URL" TARGET_HOST="$TARGET_HOST" TARGET_PORT="$TARGET_PORT" \
	./run-browser-tunnel-proxy.sh
# Debug: WASM [JSImport] partials are generated reliably; Release can fail (CS8795) on some SDK graphs.
start_bg "host" env BROWSER_HOST_PORT="$HOST_PORT" BROWSER_HOST_CONFIGURATION="${BROWSER_HOST_CONFIGURATION:-Debug}" \
	./run-browser-host.sh

HOST_WRAPPER_PID="${pids[$(( ${#pids[@]} - 1 ))]}"
echo
echo "== Waiting for browser host on port ${HOST_PORT} (first build can take 1–2 min) =="
if ! wait_for_port_or_fail "$HOST_PORT" "$HOST_WRAPPER_PID" 120; then
	exit 1
fi

JOIN_URL="$(TUNNEL_WS="$TUNNEL_WS" ./print-browser-multiplayer-join-url.sh "$HOST_URL")"
echo
echo "== Join URL =="
echo "[join] $JOIN_URL"
echo
if [ "$OPEN_BROWSER" -eq 1 ]; then
	echo "== Opening browser =="
	open_join_url "$JOIN_URL"
fi
echo "Stack running. Press Ctrl+C to stop all."

wait
