#!/usr/bin/env bash
# Copy Red Alert game content from a desktop OpenRA install into the Blazor wwwroot
# so the browser host can serve it at /support/Content/ra/v2/.
#
# Do not use the repo's top-level Resources/ folder — that is engine data (mods, glsl, …),
# not EA game assets. The only in-tree destination for RA mixes is DEST below.
# Alternative without desktop OpenRA: ./fetch-browser-game-content.sh ra  (official quickinstall ZIP).
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

DEST="OpenRA.BrowserHost/wwwroot/support/Content/ra/v2"

# Overrides:
#   SRC=/path/to/Content/ra/v2 ./copy-ra-content-to-browser.sh
#   OPENRA_APP="/path/to/OpenRA - Red Alert.app" ./copy-ra-content-to-browser.sh
# Optional: OPENRA_FIND_MIX=1  → Spotlight search for allies.mix (needs disk access on macOS)

SRC_CANDIDATES=()

add_candidate_dir() {
	local d="$1"
	[[ -n "$d" ]] || return 0
	[[ -d "$d" ]] || return 0
	SRC_CANDIDATES+=("$d")
}

if [[ -z "${SRC:-}" ]]; then
	if [[ "$(uname -s)" == "Darwin" ]]; then
		OPENRA_APP="${OPENRA_APP:-/Applications/OpenRA - Red Alert.app}"

		if [[ -d "$OPENRA_APP/Contents/Resources" ]]; then
			add_candidate_dir "$OPENRA_APP/Contents/Resources/Content/ra/v2"
			add_candidate_dir "$OPENRA_APP/Contents/Resources/OpenRA/Content/ra/v2"
			MIX_IN_RESOURCES=$(find "$OPENRA_APP/Contents/Resources" -name allies.mix -type f 2>/dev/null | head -1 || true)
			if [[ -n "$MIX_IN_RESOURCES" ]]; then
				SRC_CANDIDATES+=("$(dirname "$MIX_IN_RESOURCES")")
			fi
		fi

		for d in \
			"$OPENRA_APP/Contents/MacOS/Content/ra/v2" \
			"$HOME/Library/Application Support/OpenRA/Content/ra/v2" \
			"$HOME/Library/Application Support/OpenRA Dev/Content/ra/v2" \
			"$HOME/Library/Application Support/openra/Content/ra/v2" \
			"$HOME/.openra/Content/ra/v2" \
			"$HOME/Library/Containers/net.openra.mod.ra/Data/Library/Application Support/OpenRA/Content/ra/v2"
		do
			add_candidate_dir "$d"
		done
	else
		SRC_CANDIDATES+=(
			"${XDG_CONFIG_HOME:-$HOME/.config}/openra/Content/ra/v2"
			"$HOME/.openra/Content/ra/v2"
		)
	fi

	SRC=""
	if [[ ${#SRC_CANDIDATES[@]} -gt 0 ]]; then
		for d in "${SRC_CANDIDATES[@]}"; do
			[[ -z "$d" ]] && continue
			if [[ -d "$d" && -f "$d/allies.mix" ]]; then
				SRC="$d"
				break
			fi
		done
	fi

	# Last resort (macOS): Spotlight — may return nothing if indexing/TCC blocks it
	if [[ -z "$SRC" && "$(uname -s)" == "Darwin" && -n "${OPENRA_FIND_MIX:-}" ]]; then
		while IFS= read -r mixpath; do
			[[ -f "$mixpath" ]] || continue
			case "$mixpath" in
				*/Content/ra/v2/allies.mix)
					SRC="$(dirname "$mixpath")"
					break
					;;
			esac
		done < <(mdfind -name 'allies.mix' 2>/dev/null || true)
	fi
fi

if [[ -z "$SRC" || ! -d "$SRC" || ! -f "$SRC/allies.mix" ]]; then
	echo "No RA content found (need a folder that contains allies.mix)."
	echo ""
	echo "Copy target in this repo (browser host only):"
	echo "  $REPO_ROOT/$DEST"
	echo ""
	echo "You must install Red Alert assets in desktop OpenRA first (main menu → install content)."
	echo "Then run this script again, or copy that folder by hand."
	echo ""
	echo "Paths this script checks (macOS):"
	echo "  ~/Library/Application Support/OpenRA/Content/ra/v2"
	echo "  ~/Library/Application Support/OpenRA Dev/Content/ra/v2"
	echo "  ~/Library/Containers/net.openra.mod.ra/Data/Library/Application Support/OpenRA/Content/ra/v2"
	echo "  (and your OpenRA.app under Contents/Resources …)"
	echo ""
	echo "Find allies.mix yourself, then:"
	echo "  SRC=\"/path/to/Content/ra/v2\" $0"
	echo ""
	echo "Or try Spotlight-assisted search:"
	echo "  OPENRA_FIND_MIX=1 $0"
	echo ""
	if [[ ${#SRC_CANDIDATES[@]} -gt 0 ]]; then
		echo "Diagnostics (directories we saw):"
		for d in "${SRC_CANDIDATES[@]}"; do
			if [[ ! -d "$d" ]]; then
				echo "  (missing) $d"
			elif [[ -f "$d/allies.mix" ]]; then
				echo "  (ok)      $d"
			else
				echo "  (no mix)  $d"
			fi
		done
		echo ""
	fi
	echo "If this script fails with SRC wrongly set, run:  unset SRC"
	exit 1
fi

echo "Copying from: $SRC"
echo "         to: $(pwd)/$DEST"
mkdir -p "$DEST"
rsync -a --delete "$SRC/" "$DEST/"
echo "Done. Rebuild/run BrowserHost and hard-refresh the browser."
echo "Note: OpenRA.BrowserHost/wwwroot/support/ is gitignored — keep EA assets local or on your deploy host, not in git."
