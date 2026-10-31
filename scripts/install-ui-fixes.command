#!/bin/sh
# Optional: install Dziggy's Ultrawide UI Fixes (Nexus mod 283) loaded through me3, so loading
# screens, the death transition, the map, the site-of-grace fade and menus fill the screen.
#
#   ./scripts/install-ui-fixes.command [--bottle PATH] [--ratio 32x9] [--hud 16x9|21x9|32x9|none] FOLDER
#
# FOLDER holds the zips downloaded from https://www.nexusmods.com/eldenring/mods/283?tab=files :
#   required  the main file for your ratio, e.g. "04 - 32x9-283-2-0-2-….zip"
#   optional  "Alternate HUD Ratios-283-….zip"        (default: keep the HUD in a 16:9 area)
#   optional  "World Map Border Loading Fix-283-….zip" (removes map pop-in at the screen edges)
# me3 v0.13.0 is downloaded from GitHub and checked against a pinned SHA-256.

set -eu

SCRIPT_DIRECTORY=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd -P)
# shellcheck source=scripts/common.sh
. "$SCRIPT_DIRECTORY/common.sh"

ME3_URL='https://github.com/garyttierney/me3/releases/download/v0.13.0/me3-windows-amd64.zip'
ME3_SHA256='a8b693c574106f20532ab1e8b58b2452f37370679b938a884fe75f87f9b68c2b'

usage()
{
    printf 'Usage: %s [--bottle PATH] [--ratio 21.33x9|21.5x9|21.6x9|32x9|48x9] [--hud 16x9|21x9|32x9|none] FOLDER\n' "$0"
}

BOTTLE_PATH=''
RATIO='32x9'
HUD='16x9'
FOLDER=''
while [ "$#" -gt 0 ]
do
    case "$1" in
        --bottle) [ "$#" -ge 2 ] || fail '--bottle requires a path.'; BOTTLE_PATH=$2; shift 2 ;;
        --ratio) [ "$#" -ge 2 ] || fail '--ratio requires a value.'; RATIO=$2; shift 2 ;;
        --hud) [ "$#" -ge 2 ] || fail '--hud requires a value.'; HUD=$2; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        -*) usage >&2; fail "Unknown argument: $1" ;;
        *) FOLDER=$1; shift ;;
    esac
done
[ -n "$FOLDER" ] && [ -d "$FOLDER" ] || { usage >&2; fail 'Give the folder containing the Ultrawide UI Fixes zips.'; }

# Dziggy names the main files and the map-fix folders slightly differently per ratio.
case "$RATIO" in
    21.33x9) BASE_TOKEN='21.33x9'; MAP_TOKEN='21.33x9' ;;
    21.5x9)  BASE_TOKEN='21.5x9';  MAP_TOKEN='21.50x9' ;;
    21.6x9)  BASE_TOKEN='21.60x9'; MAP_TOKEN='21.60x9' ;;
    32x9)    BASE_TOKEN='32x9';    MAP_TOKEN='32x9' ;;
    48x9)    BASE_TOKEN='48x9';    MAP_TOKEN='48x9' ;;
    *) fail "Unsupported --ratio '$RATIO'." ;;
esac
case "$HUD" in 16x9|21x9|32x9|none) ;; *) fail "Unsupported --hud '$HUD'." ;; esac

BOTTLE_PATH=$(resolve_bottle)
TOOLS="$BOTTLE_PATH/drive_c/EldenRingTools"
ensure_game_closed "$BOTTLE_PATH"
[ -f "$TOOLS/EldenRingUltrawideLauncher.exe" ] || fail 'Run ./scripts/install.command first.'

base_zip=$(find "$FOLDER" -maxdepth 1 -name "*- ${BASE_TOKEN}*-283-*.zip" | head -1)
[ -n "$base_zip" ] || fail "No main Ultrawide UI Fixes zip for $RATIO in $FOLDER (expected e.g. \"04 - 32x9-283-….zip\")."
hud_zip=$(find "$FOLDER" -maxdepth 1 -name 'Alternate HUD Ratios-283-*.zip' | head -1)
map_zip=$(find "$FOLDER" -maxdepth 1 -name 'World Map Border Loading Fix-283-*.zip' | head -1)

work=$(mktemp -d -t ercu-ui.XXXXXX)
trap 'rm -rf "$work"' EXIT HUP INT TERM

# Only menu layout files are expected; refuse anything else.
for zip in "$base_zip" ${hud_zip:+"$hud_zip"} ${map_zip:+"$map_zip"}
do
    unexpected=$(unzip -Z1 "$zip" | grep -v -E '/$|\.gfx$|ReadMe\.txt$' || true)
    [ -z "$unexpected" ] || fail "Unexpected files in $(basename "$zip"): $unexpected"
done

mkdir -p "$work/base" "$work/hud" "$work/map" "$work/me3"
unzip -q "$base_zip" -d "$work/base"
[ -d "$work/base/menu" ] || fail "$(basename "$base_zip") has no menu folder."
if [ "$HUD" != none ] && [ -n "$hud_zip" ]; then
    unzip -q "$hud_zip" -d "$work/hud"
    [ -d "$work/hud/Use $HUD HUD/menu" ] || fail "No \"Use $HUD HUD\" set in $(basename "$hud_zip")."
    cp "$work/hud/Use $HUD HUD/menu/"*.gfx "$work/base/menu/"
    printf 'HUD layout: %s\n' "$HUD"
fi
if [ -n "$map_zip" ]; then
    unzip -q "$map_zip" -d "$work/map"
    map_file=$(find "$work/map" -path "*/${MAP_TOKEN}*/menu/02_120_worldmap.gfx" | head -1)
    [ -n "$map_file" ] && cp "$map_file" "$work/base/menu/" && printf 'World map border loading fix: %s\n' "$MAP_TOKEN"
fi

printf 'Downloading me3 v0.13.0...\n'
curl -fsSL "$ME3_URL" -o "$work/me3.zip"
[ "$(shasum -a 256 "$work/me3.zip" | awk '{print $1}')" = "$ME3_SHA256" ] || fail 'me3 download checksum mismatch.'
unzip -q "$work/me3.zip" -d "$work/me3"

rm -rf "$TOOLS/me3" "$TOOLS/ui-fixes" "$TOOLS/ui-fixes.off"
mkdir -p "$TOOLS/ui-fixes"
cp -R "$work/me3" "$TOOLS/me3"
cp -R "$work/base/menu" "$TOOLS/ui-fixes/menu"

printf '\nUltrawide UI Fixes installed (%s, %s files) with me3 v0.13.0.\n' "$RATIO" "$(find "$TOOLS/ui-fixes/menu" -name '*.gfx' | wc -l | tr -d ' ')"
printf 'The ELDEN RING icon now loads them on screens wider than 16:9. To switch them off, rename\n'
printf '  %s\n  to ui-fixes.off\n' "$TOOLS/ui-fixes"
