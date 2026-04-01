#!/usr/bin/env bash
# Ensure OpenRA.BrowserHost/wwwroot/support has freeware game assets (for Wasm).
# If marker files are missing, runs fetch-browser-game-content.sh (network).
#
# Usage: ./ensure-browser-game-content.sh ra|cnc|all
# Environment:
#   SKIP_BROWSER_GAME_CONTENT=1  Skip check/fetch (CI or offline).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

SUPPORT_ROOT="${BROWSER_SUPPORT_ROOT:-$REPO_ROOT/OpenRA.BrowserHost/wwwroot/support}"

if [[ "${SKIP_BROWSER_GAME_CONTENT:-}" == "1" ]]; then
	exit 0
fi

ensure_ra() {
	[[ -f "$SUPPORT_ROOT/Content/ra/v2/allies.mix" ]] && return 0
	echo "ensure-browser-game-content: missing RA assets under $SUPPORT_ROOT; fetching quickinstall…" >&2
	"$REPO_ROOT/fetch-browser-game-content.sh" ra --dest "$SUPPORT_ROOT"
}

ensure_cnc() {
	[[ -f "$SUPPORT_ROOT/Content/cnc/tempicnh.mix" ]] && return 0
	echo "ensure-browser-game-content: missing CNC assets under $SUPPORT_ROOT; fetching basefiles…" >&2
	"$REPO_ROOT/fetch-browser-game-content.sh" cnc --dest "$SUPPORT_ROOT"
}

mod="${1:-ra}"
case "$mod" in
	ra)
		ensure_ra
		;;
	cnc)
		ensure_cnc
		;;
	all)
		ensure_ra
		ensure_cnc
		;;
	*)
		echo "ensure-browser-game-content: unknown mod '$mod' (use ra, cnc, or all)" >&2
		exit 1
		;;
esac
