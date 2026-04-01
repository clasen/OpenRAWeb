#!/usr/bin/env bash
# Download OpenRA freeware content packages (same sources as the desktop content installer)
# and lay them out under OpenRA.BrowserHost/wwwroot/support/ for the Wasm host.
#
# Keep in sync with:
#   mods/ra-content/installer/downloads.yaml  (quickinstall: SHA1, MirrorList, Extract)
#   mods/cnc-content/installer/downloads.yaml   (basefiles: SHA1, MirrorList, Extract)
#
# Usage:
#   ./fetch-browser-game-content.sh ra|cnc|all [--dest DIR] [--out DIR] [--dry-run]
#
# --dest   Support root (default: OpenRA.BrowserHost/wwwroot/support). Files go to $dest/Content/...
# --out    Alias for --dest (handy for tests, e.g. --out /tmp/ra-support-test)
# --dry-run  Print copy actions only (no network).
#
# For run scripts, use ./ensure-browser-game-content.sh instead (fetch only if missing).
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"

DEFAULT_DEST="$REPO_ROOT/OpenRA.BrowserHost/wwwroot/support"

# --- RA quickinstall (mods/ra-content/installer/downloads.yaml) ---
RA_MIRROR_LIST="https://www.openra.net/packages/ra-quickinstall-mirrors.txt"
RA_SHA1="44241f68e69db9511db82cf83c174737ccda300b"

# --- CNC basefiles (mods/cnc-content/installer/downloads.yaml) ---
CNC_MIRROR_LIST="https://www.openra.net/packages/cnc-mirrors.txt"
CNC_SHA1="72f337464963fa37d3688eb03e80eefd33669a3d"

sha1_of_file() {
	local f="$1"
	if command -v shasum >/dev/null 2>&1; then
		shasum -a 1 "$f" | awk '{print $1}'
	elif command -v sha1sum >/dev/null 2>&1; then
		sha1sum "$f" | awk '{print $1}'
	else
		echo "Need shasum (macOS) or sha1sum (Linux)." >&2
		exit 1
	fi
}

require_cmds() {
	for c in curl unzip; do
		command -v "$c" >/dev/null 2>&1 || { echo "Missing required command: $c" >&2; exit 1; }
	done
}

fetch_mirror_urls() {
	local list_url="$1"
	curl -fsSL "$list_url" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' \
		| grep -v '^#' | grep -v '^[[:space:]]*$' || true
}

download_zip_try_mirrors() {
	local list_url="$1"
	local out_zip="$2"
	local url
	while IFS= read -r url; do
		[[ -z "$url" ]] && continue
		echo "Trying mirror: $url" >&2
		if curl -fL --connect-timeout 25 --max-time 600 -o "$out_zip" "$url" 2>/dev/null; then
			if [[ -s "$out_zip" ]]; then
				echo "Downloaded: $url" >&2
				return 0
			fi
		fi
		echo "  (failed or empty)" >&2
	done < <(fetch_mirror_urls "$list_url")
	return 1
}

verify_sha1() {
	local file="$1"
	local want="$2"
	local got
	got="$(sha1_of_file "$file" | tr '[:upper:]' '[:lower:]')"
	want="$(echo "$want" | tr '[:upper:]' '[:lower:]')"
	if [[ "$got" != "$want" ]]; then
		echo "SHA1 mismatch for $file" >&2
		echo "  expected: $want" >&2
		echo "  actual:   $got" >&2
		return 1
	fi
	echo "SHA1 OK ($got)" >&2
}

# dest_under_support = path under support root, e.g. Content/ra/v2/allies.mix
# zip_entry = path inside archive
install_from_stage() {
	local stage="$1"
	local support_root="$2"
	local dry="$3"
	shift 3
	local pair dest zipent src dst
	while (($# >= 2)); do
		dest="$1"
		zipent="$2"
		shift 2
		src="$stage/$zipent"
		dst="$support_root/$dest"
		if [[ ! -e "$src" ]]; then
			echo "ERROR: zip missing entry: $zipent (expected at $src)" >&2
			return 1
		fi
		if [[ "$dry" == 1 ]]; then
			echo "cp $src -> $dst"
			continue
		fi
		mkdir -p "$(dirname "$dst")"
		cp -f "$src" "$dst"
	done
}

fetch_ra() {
	local support_root="$1"
	local dry="$2"
	if [[ "$dry" == 1 ]]; then
		echo "DRY RUN: would download RA quickinstall (SHA1 $RA_SHA1) via $RA_MIRROR_LIST" >&2
		local i
		for ((i = 0; i < ${#RA_EXTRACT_PAIRS[@]}; i += 2)); do
			echo "cp <zip>/${RA_EXTRACT_PAIRS[i + 1]} -> $support_root/${RA_EXTRACT_PAIRS[i]}"
		done
		return 0
	fi
	local zip tmpdir stage
	tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/openra-ra-content.XXXXXX")"
	zip="$tmpdir/archive.zip"
	stage="$tmpdir/stage"
	mkdir -p "$stage"
	echo "Fetching RA quickinstall…" >&2
	if ! download_zip_try_mirrors "$RA_MIRROR_LIST" "$zip"; then
		echo "All RA mirrors failed." >&2
		rm -rf "$tmpdir"
		return 1
	fi
	if ! verify_sha1 "$zip" "$RA_SHA1"; then
		rm -rf "$tmpdir"
		return 1
	fi
	unzip -q -o "$zip" -d "$stage" || { rm -rf "$tmpdir"; return 1; }
	if ! install_from_stage "$stage" "$support_root" "$dry" "${RA_EXTRACT_PAIRS[@]}"; then
		rm -rf "$tmpdir"
		return 1
	fi
	rm -rf "$tmpdir"
	echo "RA content installed under $support_root/Content/ra/v2" >&2
}

fetch_cnc() {
	local support_root="$1"
	local dry="$2"
	if [[ "$dry" == 1 ]]; then
		echo "DRY RUN: would download CNC basefiles (SHA1 $CNC_SHA1) via $CNC_MIRROR_LIST" >&2
		local i
		for ((i = 0; i < ${#CNC_EXTRACT_PAIRS[@]}; i += 2)); do
			echo "cp <zip>/${CNC_EXTRACT_PAIRS[i + 1]} -> $support_root/${CNC_EXTRACT_PAIRS[i]}"
		done
		return 0
	fi
	local zip tmpdir stage
	tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/openra-cnc-content.XXXXXX")"
	zip="$tmpdir/archive.zip"
	stage="$tmpdir/stage"
	mkdir -p "$stage"
	echo "Fetching CNC basefiles…" >&2
	if ! download_zip_try_mirrors "$CNC_MIRROR_LIST" "$zip"; then
		echo "All CNC mirrors failed." >&2
		rm -rf "$tmpdir"
		return 1
	fi
	if ! verify_sha1 "$zip" "$CNC_SHA1"; then
		rm -rf "$tmpdir"
		return 1
	fi
	unzip -q -o "$zip" -d "$stage" || { rm -rf "$tmpdir"; return 1; }
	if ! install_from_stage "$stage" "$support_root" "$dry" "${CNC_EXTRACT_PAIRS[@]}"; then
		rm -rf "$tmpdir"
		return 1
	fi
	rm -rf "$tmpdir"
	echo "CNC content installed under $support_root/Content/cnc" >&2
}

# Pairs: destination_relative_to_support_root zip_entry
# Sync with mods/ra-content/installer/downloads.yaml quickinstall Extract (^SupportDir| stripped).
RA_EXTRACT_PAIRS=(
	Content/ra/v2/allies.mix allies.mix
	Content/ra/v2/conquer.mix conquer.mix
	Content/ra/v2/hires.mix hires.mix
	Content/ra/v2/interior.mix interior.mix
	Content/ra/v2/local.mix local.mix
	Content/ra/v2/lores.mix lores.mix
	Content/ra/v2/russian.mix russian.mix
	Content/ra/v2/snow.mix snow.mix
	Content/ra/v2/sounds.mix sounds.mix
	Content/ra/v2/speech.mix speech.mix
	Content/ra/v2/temperat.mix temperat.mix
	Content/ra/v2/expand/chrotnk1.aud expand/chrotnk1.aud
	Content/ra/v2/expand/expand2.mix expand/expand2.mix
	Content/ra/v2/expand/fixit1.aud expand/fixit1.aud
	Content/ra/v2/expand/hires1.mix expand/hires1.mix
	Content/ra/v2/expand/jburn1.aud expand/jburn1.aud
	Content/ra/v2/expand/jchrge1.aud expand/jchrge1.aud
	Content/ra/v2/expand/jcrisp1.aud expand/jcrisp1.aud
	Content/ra/v2/expand/jdance1.aud expand/jdance1.aud
	Content/ra/v2/expand/jjuice1.aud expand/jjuice1.aud
	Content/ra/v2/expand/jjump1.aud expand/jjump1.aud
	Content/ra/v2/expand/jlight1.aud expand/jlight1.aud
	Content/ra/v2/expand/jpower1.aud expand/jpower1.aud
	Content/ra/v2/expand/jshock1.aud expand/jshock1.aud
	Content/ra/v2/expand/jyes1.aud expand/jyes1.aud
	Content/ra/v2/expand/lores1.mix expand/lores1.mix
	Content/ra/v2/expand/madchrg2.aud expand/madchrg2.aud
	Content/ra/v2/expand/madexplo.aud expand/madexplo.aud
	Content/ra/v2/expand/mboss1.aud expand/mboss1.aud
	Content/ra/v2/expand/mhear1.aud expand/mhear1.aud
	Content/ra/v2/expand/mhotdig1.aud expand/mhotdig1.aud
	Content/ra/v2/expand/mhowdy1.aud expand/mhowdy1.aud
	Content/ra/v2/expand/mhuh1.aud expand/mhuh1.aud
	Content/ra/v2/expand/mlaff1.aud expand/mlaff1.aud
	Content/ra/v2/expand/mrise1.aud expand/mrise1.aud
	Content/ra/v2/expand/mwrench1.aud expand/mwrench1.aud
	Content/ra/v2/expand/myeehaw1.aud expand/myeehaw1.aud
	Content/ra/v2/expand/myes1.aud expand/myes1.aud
	Content/ra/v2/cnc/desert.mix cnc/desert.mix
)

# Sync with mods/cnc-content/installer/downloads.yaml basefiles Extract.
CNC_EXTRACT_PAIRS=(
	Content/cnc/conquer.mix conquer.mix
	Content/cnc/desert.mix desert.mix
	Content/cnc/general.mix general.mix
	Content/cnc/sounds.mix sounds.mix
	Content/cnc/speech.mix speech.mix
	Content/cnc/temperat.mix temperat.mix
	Content/cnc/tempicnh.mix tempicnh.mix
	Content/cnc/transit.mix transit.mix
	Content/cnc/winter.mix winter.mix
)

usage() {
	sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
	exit "${1:-0}"
}

main() {
	if [[ "${1:-}" == "" || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
		usage 0
	fi

	local cmd="$1"
	shift
	local support_root="$DEFAULT_DEST"
	local dry=0

	while [[ $# -gt 0 ]]; do
		case "$1" in
			--dest)
				support_root="${2%/}"
				shift 2
				;;
			--out)
				support_root="${2%/}"
				shift 2
				;;
			--dry-run)
				dry=1
				shift
				;;
			-h|--help)
				usage 0
				;;
			*)
				echo "Unknown option: $1" >&2
				usage 1
				;;
		esac
	done

	if [[ "$support_root" != /* ]]; then
		support_root="$REPO_ROOT/$support_root"
	fi

	require_cmds

	case "$cmd" in
		ra)
			mkdir -p "$support_root"
			fetch_ra "$support_root" "$dry"
			;;
		cnc)
			mkdir -p "$support_root"
			fetch_cnc "$support_root" "$dry"
			;;
		all)
			mkdir -p "$support_root"
			fetch_ra "$support_root" "$dry"
			fetch_cnc "$support_root" "$dry"
			;;
		*)
			echo "First arg must be ra, cnc, or all." >&2
			usage 1
			;;
	esac
}

main "$@"
