#!/usr/bin/env bash
# Copy Tiberian Dawn (CnC) game content from a desktop OpenRA install into the Blazor wwwroot
# so the browser host can serve it at /support/Content/cnc/.
#
# Do not use the repo's top-level Resources/ folder — that is engine data (mods, glsl, …),
# not EA game assets. Alternative without desktop OpenRA: ./fetch-browser-game-content.sh cnc
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

DEST="OpenRA.BrowserHost/wwwroot/support/Content/cnc"

# Overrides:
#   SRC=/path/to/Content/cnc ./copy-cnc-content-to-browser.sh
#   OPENRA_APP="/path/to/OpenRA - Tiberian Dawn.app" ./copy-cnc-content-to-browser.sh
# Optional: OPENRA_FIND_MIX=1  → Spotlight search for tempicnh.mix (needs disk access on macOS)

SRC_CANDIDATES=()

add_candidate_dir() {
	local d="$1"
	[[ -n "$d" ]] || return 0
	[[ -d "$d" ]] || return 0
	SRC_CANDIDATES+=("$d")
}

if [[ -z "${SRC:-}" ]]; then
	if [[ "$(uname -s)" == "Darwin" ]]; then
		OPENRA_APP="${OPENRA_APP:-/Applications/OpenRA - Tiberian Dawn.app}"

		if [[ -d "$OPENRA_APP/Contents/Resources" ]]; then
			add_candidate_dir "$OPENRA_APP/Contents/Resources/Content/cnc"
			add_candidate_dir "$OPENRA_APP/Contents/Resources/OpenRA/Content/cnc"
			MIX_IN_RESOURCES=$(find "$OPENRA_APP/Contents/Resources" -name tempicnh.mix -type f 2>/dev/null | head -1 || true)
			if [[ -n "$MIX_IN_RESOURCES" ]]; then
				SRC_CANDIDATES+=("$(dirname "$MIX_IN_RESOURCES")")
			fi
		fi

		for d in \
			"$OPENRA_APP/Contents/MacOS/Content/cnc" \
			"$HOME/Library/Application Support/OpenRA/Content/cnc" \
			"$HOME/Library/Application Support/OpenRA Dev/Content/cnc" \
			"$HOME/Library/Application Support/openra/Content/cnc" \
			"$HOME/.openra/Content/cnc" \
			"$HOME/Library/Containers/net.openra.mod.cnc/Data/Library/Application Support/OpenRA/Content/cnc"
		do
			add_candidate_dir "$d"
		done
	else
		SRC_CANDIDATES+=(
			"${XDG_CONFIG_HOME:-$HOME/.config}/openra/Content/cnc"
			"$HOME/.openra/Content/cnc"
		)
	fi

	SRC=""
	if [[ ${#SRC_CANDIDATES[@]} -gt 0 ]]; then
		for d in "${SRC_CANDIDATES[@]}"; do
			[[ -z "$d" ]] && continue
			if [[ -d "$d" && -f "$d/tempicnh.mix" ]]; then
				SRC="$d"
				break
			fi
		done
	fi

	if [[ -z "$SRC" && "$(uname -s)" == "Darwin" && -n "${OPENRA_FIND_MIX:-}" ]]; then
		while IFS= read -r mixpath; do
			[[ -f "$mixpath" ]] || continue
			case "$mixpath" in
				*/Content/cnc/tempicnh.mix)
					SRC="$(dirname "$mixpath")"
					break
					;;
			esac
		done < <(mdfind -name 'tempicnh.mix' 2>/dev/null || true)
	fi
fi

if [[ -z "$SRC" || ! -d "$SRC" || ! -f "$SRC/tempicnh.mix" ]]; then
	echo "No C&C (TD) content found (need a folder that contains tempicnh.mix)."
	echo ""
	echo "Copy target in this repo (browser host only):"
	echo "  $REPO_ROOT/$DEST"
	echo ""
	echo "Install Tiberian Dawn assets in desktop OpenRA first (main menu → install content)."
	echo "Then run this script again, or copy that folder by hand."
	echo ""
	echo "Paths this script checks (macOS):"
	echo "  ~/Library/Application Support/OpenRA/Content/cnc"
	echo "  ~/Library/Application Support/OpenRA Dev/Content/cnc"
	echo "  ~/Library/Containers/net.openra.mod.cnc/Data/Library/Application Support/OpenRA/Content/cnc"
	echo "  (and your OpenRA.app under Contents/Resources …)"
	echo ""
	echo "Find tempicnh.mix yourself, then:"
	echo "  SRC=\"/path/to/Content/cnc\" $0"
	echo ""
	echo "Or try Spotlight-assisted search:"
	echo "  OPENRA_FIND_MIX=1 $0"
	echo ""
	if [[ ${#SRC_CANDIDATES[@]} -gt 0 ]]; then
		echo "Diagnostics (directories we saw):"
		for d in "${SRC_CANDIDATES[@]}"; do
			if [[ ! -d "$d" ]]; then
				echo "  (missing) $d"
			elif [[ -f "$d/tempicnh.mix" ]]; then
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
