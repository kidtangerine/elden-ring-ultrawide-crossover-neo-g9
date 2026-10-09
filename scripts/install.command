#!/bin/sh

set -eu

SCRIPT_DIRECTORY=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd -P)
# shellcheck source=scripts/common.sh
. "$SCRIPT_DIRECTORY/common.sh"

usage()
{
    printf 'Usage: %s [--bottle "/full/path/to/Elden Ring bottle"] [--resolution auto|WIDTHxHEIGHT]\n' "$0"
}

BOTTLE_PATH=''
RESOLUTION='auto'
while [ "$#" -gt 0 ]
do
    case "$1" in
        --bottle)
            [ "$#" -ge 2 ] || fail '--bottle requires a path.'
            BOTTLE_PATH=$2
            shift 2
            ;;
        --resolution)
            [ "$#" -ge 2 ] || fail '--resolution requires WIDTHxHEIGHT, e.g. 5120x1440.'
            RESOLUTION=$2
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            fail "Unknown argument: $1"
            ;;
    esac
done

case "$RESOLUTION" in
    auto) ;;
    [1-9][0-9][0-9]*x[1-9][0-9][0-9]*) ;;
    *) fail "Invalid --resolution '$RESOLUTION'; expected WIDTHxHEIGHT, e.g. 5120x1440." ;;
esac
if [ "$RESOLUTION" != auto ]; then
    case "${RESOLUTION%%x*}:${RESOLUTION#*x}" in
        *[!0-9:]*) fail "Invalid --resolution '$RESOLUTION'." ;;
    esac
fi

ROOT=$(project_root)
BOTTLE_PATH=$(resolve_bottle)
BOTTLE_NAME=$(basename "$BOTTLE_PATH")
WINE=$(find_crossover_wine)
TOOLS="$BOTTLE_PATH/drive_c/EldenRingTools"
WINDOWS_START_MENU="$BOTTLE_PATH/drive_c/users/crossover/AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Steam/ELDEN RING.url"
WINDOWS_DESKTOP="$BOTTLE_PATH/drive_c/users/crossover/Desktop/ELDEN RING.url"
CXMENU="$BOTTLE_PATH/desktopdata/cxmenu"

case "$BOTTLE_NAME$WINE" in
    *\"*|*\`*) fail 'Bottle and CrossOver paths may not contain quotes or backticks.' ;;
esac

require_supported_game "$BOTTLE_PATH"
ensure_game_closed "$BOTTLE_PATH"

launcher_hash=$(shasum -a 256 "$ROOT/dist/EldenRingUltrawideLauncher.exe" | awk '{print $1}')
expected_launcher_hash='ff6dd583529a0dda082ab685c10a8a9acc812068efdfff187d79463117ce4c43'
[ "$launcher_hash" = "$expected_launcher_hash" ] || fail 'Bundled launcher checksum mismatch; obtain a clean repository copy.'

timestamp=$(date '+%Y%m%d-%H%M%S')
backup="$TOOLS/backups/$timestamp"
mkdir -p "$backup/cxmenu" "$TOOLS" "$(dirname "$WINDOWS_START_MENU")" "$(dirname "$WINDOWS_DESKTOP")"

if [ -f "$WINDOWS_START_MENU" ]; then
    cp -p "$WINDOWS_START_MENU" "$backup/windows-start-menu.url"
else
    : > "$backup/windows-start-menu.was-missing"
fi
if [ -f "$WINDOWS_DESKTOP" ]; then
    cp -p "$WINDOWS_DESKTOP" "$backup/windows-desktop.url"
else
    : > "$backup/windows-desktop.was-missing"
fi

if [ -d "$CXMENU" ]; then
    find "$CXMENU" -maxdepth 1 -type f -name '*ELDEN+RING.url' -exec cp -p '{}' "$backup/cxmenu/" ';'
fi

cp "$ROOT/dist/EldenRingUltrawideLauncher.exe" "$TOOLS/EldenRingUltrawideLauncher.exe"
cp "$ROOT/resources/install.reg" "$TOOLS/install.reg"
printf '%s' "$RESOLUTION" > "$TOOLS/resolution.txt"
cp "$ROOT/resources/ELDEN RING.url" "$WINDOWS_START_MENU"
cp "$ROOT/resources/ELDEN RING.url" "$WINDOWS_DESKTOP"

wrapper="$TOOLS/launch-elden-ring-ultrawide.command"
{
    printf '%s\n' '#!/bin/sh' 'set -eu' ''
    printf 'exec "%s" --bottle "%s" --check --wait-children --start "%s"\n' \
        "$WINE" "$BOTTLE_NAME" 'C:/users/crossover/AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Steam/ELDEN RING.url'
} > "$wrapper"
chmod 755 "$wrapper"

if [ -d "$CXMENU" ]; then
    find "$CXMENU" -maxdepth 1 -type f -name '*ELDEN+RING.url' -exec cp "$wrapper" '{}' ';'
    find "$CXMENU" -maxdepth 1 -type f -name '*ELDEN+RING.url' -exec chmod 755 '{}' ';'
fi

"$WINE" --bottle "$BOTTLE_NAME" regedit /S 'C:\EldenRingTools\install.reg'
printf '%s\n' "$backup" > "$TOOLS/latest-backup.txt"

printf '\nInstalled successfully.\n'
printf 'Bottle: %s\n' "$BOTTLE_PATH"
printf 'Backup: %s\n' "$backup"
printf 'Resolution: %s (on screens wider than 16:9; pick it in-game under System > Graphics)\n' "$RESOLUTION"
printf 'Launch from the normal CrossOver Elden Ring icon. Steam starts automatically if needed.\n'
