#!/usr/bin/env bash
# All-in-one: browser multiplayer stack + ngrok public URLs (friend joins via HTTPS/WSS).
# Requires ngrok installed and configured (e.g. ngrok config add-authtoken …).
#
# Default: dedicated + WebSocket tunnel proxy + Blazor host + two "ngrok http" tunnels,
#          then prints a join URL with ?tunnel=wss://… for the remote browser.
#
# Usage:
#   ./run-browser-multiplayer-ngrok.sh
#   ./run-browser-multiplayer-ngrok.sh --mod cnc --open
#   ./run-browser-multiplayer-ngrok.sh --no-build -- Server.Name="Public"
#   ./run-browser-multiplayer-ngrok.sh --tcp-only [--mod ra]   # desktop friend: ngrok tcp only
#
# Environment (same as run-browser-multiplayer.sh where applicable):
#   BROWSER_HOST_PORT, BROWSER_HOST_CONFIGURATION, LISTEN, TARGET_HOST, TARGET_PORT
#   NGROK_API_ADDR     default 127.0.0.1:4040 (local agent API)
#   NGROK_CONFIG       optional extra --config path (merged before fragment)
set -euo pipefail
# Background jobs get their own process groups so Ctrl+C can kill the whole tree (dotnet, ngrok, etc.).
set -m

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

MOD="ra"
DO_BUILD=1
OPEN_BROWSER=0
TCP_ONLY=0
DEDICATED_ARGS=()

usage() {
	cat <<'EOF'
run-browser-multiplayer-ngrok.sh

Starts OpenRA browser multiplayer locally and exposes it with ngrok.

Options:
  --mod <id>      Dedicated mod (default: ra)
  --no-build      Skip ./build-browser-multiplayer-local.sh (browser stack only)
  --open          Open the public join URL in the default browser
  --tcp-only      Only dedicated server + "ngrok tcp" (friend uses desktop OpenRA direct connect)
  --help          Show this help
  --              Pass remaining args to run-dedicated-server.sh

Examples:
  ./run-browser-multiplayer-ngrok.sh
  ./run-browser-multiplayer-ngrok.sh --open --mod cnc
  ./run-browser-multiplayer-ngrok.sh --tcp-only
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
		--tcp-only)
			TCP_ONLY=1
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

if ! command -v ngrok >/dev/null 2>&1; then
	echo "run-browser-multiplayer-ngrok: ngrok not found in PATH." >&2
	exit 1
fi

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
	echo "run-browser-multiplayer-ngrok: no free TCP port between $start and $max" >&2
	return 1
}

listen_url_to_port() {
	local listen_url="$1"
	python3 - "$listen_url" <<'PY'
import sys
from urllib.parse import urlparse
u = urlparse(sys.argv[1])
if not u.port:
    print(80 if u.scheme == "http" else 443)
else:
    print(u.port)
PY
}

wait_for_port_or_fail() {
	local port=$1
	local watch_pid=$2
	local max_wait="${3:-90}"
	local i=0
	while [ "$i" -lt "$max_wait" ]; do
		if ! kill -0 "$watch_pid" 2>/dev/null; then
			echo "run-browser-multiplayer-ngrok: process exited before port $port was ready." >&2
			return 1
		fi
		if port_is_listening "$port"; then
			return 0
		fi
		sleep 1
		i=$((i + 1))
	done
	echo "run-browser-multiplayer-ngrok: timed out waiting for port ${port}." >&2
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
	python3 - "$listen_url" <<'PY'
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
print(f"{scheme}://{u.netloc}{path}")
PY
}

ngrok_merge_config_args() {
	local args=()
	if [[ -n "${NGROK_CONFIG:-}" ]]; then
		args+=(--config "$NGROK_CONFIG")
	fi
	if [[ -f "${HOME}/.config/ngrok/ngrok.yml" ]]; then
		args+=(--config "${HOME}/.config/ngrok/ngrok.yml")
	elif [[ -f "${HOME}/Library/Application Support/ngrok/ngrok.yml" ]]; then
		args+=(--config "${HOME}/Library/Application Support/ngrok/ngrok.yml")
	fi
	printf '%s\n' "${args[@]}"
}

# Prints two lines: HTTPS base URL for host tunnel, HTTPS base URL for proxy tunnel (for wss conversion).
python3_ngrok_fetch_pair() {
	python3 - "$1" "$2" "$3" <<'PY'
import json
import sys
import urllib.error
import urllib.request

api, host_name, ws_name = sys.argv[1], sys.argv[2], sys.argv[3]

def get():
    with urllib.request.urlopen(api, timeout=2) as r:
        return json.load(r)

try:
    data = get()
except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
    sys.exit(2)

by_name = {}
for t in data.get("tunnels") or []:
    name = t.get("name") or ""
    pub = t.get("public_url") or ""
    if name and pub:
        by_name[name] = pub

h = by_name.get(host_name)
w = by_name.get(ws_name)
if not h or not w:
    sys.exit(3)
# Normalize to https URL with trailing slash for host (Blazor base)
if h.startswith("http://"):
    h = "https://" + h[len("http://") :]
if not h.endswith("/"):
    h += "/"
if w.startswith("http://"):
    w = "https://" + w[len("http://") :]
if not w.endswith("/"):
    w += "/"
print(h)
print(w)
PY
}

pids=()
NGROK_PID=""
TMP_NGROK_CFG=""

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
	echo "== Stopping stack =="
	if [[ -n "$NGROK_PID" ]]; then
		kill_proc_group "$NGROK_PID"
		wait "$NGROK_PID" 2>/dev/null || true
	fi
	for pid in "${pids[@]:-}"; do
		kill_proc_group "$pid"
	done
	for pid in "${pids[@]:-}"; do
		wait "$pid" 2>/dev/null || true
	done
	[[ -n "${TMP_NGROK_CFG:-}" && -f "$TMP_NGROK_CFG" ]] && rm -f "$TMP_NGROK_CFG"
	exit "$code"
}

trap cleanup EXIT INT TERM

# --- TCP-only: dedicated + ngrok tcp (desktop friend; no Wasm host) ---
if [[ "$TCP_ONLY" -eq 1 ]]; then
	if [[ "$DO_BUILD" -eq 1 ]]; then
		echo "== Building dedicated server (desktop) =="
		dotnet build "$REPO_ROOT/OpenRA.sln" -c Debug -p:OpenRaBrowserBuild=false -v q
	fi
	TARGET_PORT="${TARGET_PORT:-1234}"
	dedicated_cmd=(./run-dedicated-server.sh "Game.Mod=${MOD}")
	[[ ${#DEDICATED_ARGS[@]} -gt 0 ]] && dedicated_cmd+=("${DEDICATED_ARGS[@]}")
	start_bg "dedicated" "${dedicated_cmd[@]}"
	_last_pid_idx=$(( ${#pids[@]} - 1 ))
	echo "== Waiting for dedicated on port ${TARGET_PORT} =="
	if ! wait_for_port_or_fail "$TARGET_PORT" "${pids[$_last_pid_idx]}" 60; then
		exit 1
	fi
	echo "== Starting ngrok tcp ${TARGET_PORT} =="
	ngrok tcp "$TARGET_PORT" &
	NGROK_PID=$!
	sleep 2
	api="http://${NGROK_API_ADDR:-127.0.0.1:4040}/api/tunnels"
	tcp_url=""
	for _ in $(seq 1 60); do
		tcp_url="$(python3 - "$api" <<'PY' 2>/dev/null || true
import json
import sys
import urllib.request

api = sys.argv[1]
try:
    with urllib.request.urlopen(api, timeout=2) as r:
        data = json.load(r)
except Exception:
    sys.exit(1)
for t in data.get("tunnels") or []:
    u = t.get("public_url") or ""
    if u.startswith("tcp://"):
        print(u)
        raise SystemExit(0)
sys.exit(1)
PY
)"
		[[ -n "$tcp_url" ]] && break
		sleep 1
	done
	if [[ -z "${tcp_url:-}" ]]; then
		echo "run-browser-multiplayer-ngrok: could not read tcp:// URL from ngrok API (${api})." >&2
		exit 1
	fi
	echo
	echo "== Public TCP (desktop OpenRA direct connect) =="
	echo "[ngrok] $tcp_url"
	echo "Friend: use host and port from the URL above (e.g. 0.tcp.eu.ngrok.io and the assigned port)."
	echo "Press Ctrl+C to stop."
	wait
	exit 0
fi

# --- Browser stack + dual ngrok http ---
case "$MOD" in
	ra|cnc)
		./ensure-browser-game-content.sh "$MOD"
		;;
	*)
		echo "== Note: browser content auto-fetch only for mod ra|cnc (mod=$MOD) ==" >&2
		;;
esac

if [[ "$DO_BUILD" -eq 1 ]]; then
	echo "== Building browser multiplayer stack (incremental) =="
	./build-browser-multiplayer-local.sh
else
	echo "== Skipping build (--no-build) =="
fi

LISTEN_URL="${LISTEN:-http://127.0.0.1:8787}"
TARGET_HOST="${TARGET_HOST:-127.0.0.1}"
TARGET_PORT="${TARGET_PORT:-1234}"
HOST_PORT="$(pick_port)"
PROXY_PORT="$(listen_url_to_port "$LISTEN_URL")"
HOST_TUNNEL_NAME="openra-browser-host"
TUNNEL_WS_NAME="openra-browser-tunnel"

echo
echo "== Launch configuration =="
echo "mod          : $MOD"
echo "dedicated    : ${TARGET_HOST}:${TARGET_PORT}"
echo "tunnel proxy : ${LISTEN_URL} (local port ${PROXY_PORT})"
echo "browser host : http://127.0.0.1:${HOST_PORT}/"
echo "ngrok tunnels: ${HOST_TUNNEL_NAME} -> ${HOST_PORT}, ${TUNNEL_WS_NAME} -> ${PROXY_PORT}"
echo

dedicated_cmd=(./run-dedicated-server.sh "Game.Mod=${MOD}")
[[ ${#DEDICATED_ARGS[@]} -gt 0 ]] && dedicated_cmd+=("${DEDICATED_ARGS[@]}")
start_bg "dedicated" "${dedicated_cmd[@]}"
start_bg "tunnel" env LISTEN="$LISTEN_URL" TARGET_HOST="$TARGET_HOST" TARGET_PORT="$TARGET_PORT" \
	./run-browser-tunnel-proxy.sh
start_bg "host" env BROWSER_HOST_PORT="$HOST_PORT" BROWSER_HOST_CONFIGURATION="${BROWSER_HOST_CONFIGURATION:-Debug}" \
	./run-browser-host.sh

HOST_WRAPPER_PID="${pids[$(( ${#pids[@]} - 1 ))]}"
echo
echo "== Waiting for browser host on port ${HOST_PORT} =="
if ! wait_for_port_or_fail "$HOST_PORT" "$HOST_WRAPPER_PID" 120; then
	exit 1
fi

TMP_NGROK_CFG="$(mktemp "${TMPDIR:-/tmp}/openra-ngrok-XXXXXX.yml")"
cat >"$TMP_NGROK_CFG" <<EOF
version: "3"
tunnels:
  ${HOST_TUNNEL_NAME}:
    proto: http
    addr: ${HOST_PORT}
  ${TUNNEL_WS_NAME}:
    proto: http
    addr: ${PROXY_PORT}
EOF

ngrok_cfg_args=()
while IFS= read -r line; do
	[[ -n "$line" ]] && ngrok_cfg_args+=("$line")
done < <(ngrok_merge_config_args)

echo "== Starting ngrok (host + tunnel proxy) =="
set +u
if [[ ${#ngrok_cfg_args[@]} -gt 0 ]]; then
	ngrok start "$HOST_TUNNEL_NAME" "$TUNNEL_WS_NAME" "${ngrok_cfg_args[@]}" --config "$TMP_NGROK_CFG" &
else
	ngrok start "$HOST_TUNNEL_NAME" "$TUNNEL_WS_NAME" --config "$TMP_NGROK_CFG" &
fi
set -u
NGROK_PID=$!

api_base="http://${NGROK_API_ADDR:-127.0.0.1:4040}/api/tunnels"
echo "== Waiting for ngrok public URLs (API ${NGROK_API_ADDR:-127.0.0.1:4040}) =="
pair=""
for _ in $(seq 1 90); do
	if pair="$(python3_ngrok_fetch_pair "$api_base" "$HOST_TUNNEL_NAME" "$TUNNEL_WS_NAME" 2>/dev/null)"; then
		[[ -n "$pair" ]] && break
	fi
	pair=""
	sleep 1
done
if [[ -z "$pair" ]]; then
	echo "run-browser-multiplayer-ngrok: failed to read tunnel URLs from ngrok API." >&2
	echo "Check that no other ngrok agent is using port 4040, or set NGROK_API_ADDR." >&2
	exit 1
fi

HOST_HTTPS="$(echo "$pair" | sed -n '1p')"
PROXY_HTTPS="$(echo "$pair" | sed -n '2p')"
TUNNEL_WSS="$(to_ws_url "$PROXY_HTTPS")"
JOIN_URL="$(HOST_HTTP="$HOST_HTTPS" TUNNEL_WS="$TUNNEL_WSS" ./print-browser-multiplayer-join-url.sh "$HOST_HTTPS")"

LOCAL_JOIN="$(TUNNEL_WS="$(to_ws_url "$LISTEN_URL")" ./print-browser-multiplayer-join-url.sh "http://127.0.0.1:${HOST_PORT}/")"

echo
echo "== Join URLs =="
echo "[local]  $LOCAL_JOIN"
echo "[public] $JOIN_URL"
echo
if [[ "$OPEN_BROWSER" -eq 1 ]]; then
	echo "== Opening public join URL =="
	open_join_url "$JOIN_URL"
fi
echo "Stack + ngrok running. Press Ctrl+C to stop all."

wait
