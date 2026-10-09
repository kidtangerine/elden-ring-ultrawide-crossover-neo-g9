#!/bin/sh

set -eu

SCRIPT_DIRECTORY=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd -P)
# shellcheck source=scripts/common.sh
. "$SCRIPT_DIRECTORY/common.sh"

BOTTLE_PATH=''
if [ "${1:-}" = '--bottle' ]; then
    [ "$#" -eq 2 ] || fail 'Usage: uninstall.command [--bottle "/full/path/to/bottle"]'
    BOTTLE_PATH=$2
elif [ "$#" -ne 0 ]; then
    fail 'Usage: uninstall.command [--bottle "/full/path/to/bottle"]'
fi

ROOT=$(project_root)
BOTTLE_PATH=$(resolve_bottle)
BOTTLE_NAME=$(basename "$BOTTLE_PATH")
WINE=$(find_crossover_wine)
TOOLS="$BOTTLE_PATH/drive_c/EldenRingTools"
WINDOWS_START_MENU="$BOTTLE_PATH/drive_c/users/crossover/AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Steam/ELDEN RING.url"
WINDOWS_DESKTOP="$BOTTLE_PATH/drive_c/users/crossover/Desktop/ELDEN RING.url"
CXMENU="$BOTTLE_PATH/desktopdata/cxmenu"

ensure_game_closed "$BOTTLE_PATH"
[ -f "$TOOLS/latest-backup.txt" ] || fail 'No installer backup record was found.'
backup=$(sed -n '1p' "$TOOLS/latest-backup.txt")
case "$backup" in
    "$TOOLS"/backups/*) ;;
    *) fail 'The recorded backup path is invalid.' ;;
esac
[ -d "$backup" ] || fail "Recorded backup directory not found: $backup"

if [ -f "$backup/windows-start-menu.url" ]; then
    cp -p "$backup/windows-start-menu.url" "$WINDOWS_START_MENU"
else
    rm -f "$WINDOWS_START_MENU"
fi
if [ -f "$backup/windows-desktop.url" ]; then
    cp -p "$backup/windows-desktop.url" "$WINDOWS_DESKTOP"
else
    rm -f "$WINDOWS_DESKTOP"
fi

if [ -d "$backup/cxmenu" ] && [ -d "$CXMENU" ]; then
    for saved in "$backup/cxmenu"/*
    do
        [ -f "$saved" ] || continue
        cp -p "$saved" "$CXMENU/$(basename "$saved")"
    done
fi

cp "$ROOT/resources/uninstall.reg" "$TOOLS/uninstall.reg"
"$WINE" --bottle "$BOTTLE_NAME" regedit /S 'C:\EldenRingTools\uninstall.reg'
rm -f "$TOOLS/EldenRingUltrawideLauncher.exe" "$TOOLS/install.reg" "$TOOLS/uninstall.reg" \
    "$TOOLS/launch-elden-ring-ultrawide.command" "$TOOLS/elden-ring-ultrawide.log" "$TOOLS/latest-backup.txt" \
    "$TOOLS/resolution.txt" "$BOTTLE_PATH/$GAME_RELATIVE/steam_appid.txt"

printf 'Uninstalled. Original URL/menu files were restored from: %s\n' "$backup"
