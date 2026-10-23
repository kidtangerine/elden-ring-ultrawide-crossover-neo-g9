#!/bin/sh

set -eu

GAME_RELATIVE='drive_c/Program Files (x86)/Steam/steamapps/common/ELDEN RING/Game'

fail()
{
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

project_root()
{
    CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd -P
}

find_crossover_wine()
{
    if [ -n "${CROSSOVER_APP:-}" ]; then
        candidate="$CROSSOVER_APP/Contents/SharedSupport/CrossOver/bin/wine"
        [ -x "$candidate" ] || fail "CROSSOVER_APP does not point to a usable CrossOver installation."
        printf '%s\n' "$candidate"
        return
    fi

    for app in '/Applications/CrossOver.app' "$HOME/Applications/CrossOver.app"
    do
        candidate="$app/Contents/SharedSupport/CrossOver/bin/wine"
        if [ -x "$candidate" ]; then
            printf '%s\n' "$candidate"
            return
        fi
    done
    fail 'CrossOver.app was not found. Set CROSSOVER_APP to its full path.'
}

validate_bottle()
{
    requested=$1
    [ -d "$requested" ] || fail "Bottle directory not found: $requested"
    resolved=$(CDPATH='' cd -- "$requested" && pwd -P)
    [ "$resolved" != '/' ] || fail 'Refusing to use the filesystem root as a bottle.'
    [ -f "$resolved/$GAME_RELATIVE/eldenring.exe" ] || \
        fail "Elden Ring was not found in the standard Steam location inside: $resolved"
    printf '%s\n' "$resolved"
}

discover_bottle()
{
    result_file=$(mktemp -t ercu-bottles.XXXXXX)
    trap 'rm -f "$result_file"' EXIT HUP INT TERM

    standard_root="$HOME/Library/Application Support/CrossOver/Bottles"
    if [ -d "$standard_root" ]; then
        for candidate in "$standard_root"/*
        do
            resolved_candidate=$(CDPATH='' cd -- "$candidate" 2>/dev/null && pwd -P) || continue
            if [ -f "$resolved_candidate/$GAME_RELATIVE/eldenring.exe" ]; then
                printf '%s\n' "$resolved_candidate/$GAME_RELATIVE/eldenring.exe" >> "$result_file"
            fi
        done
    fi

    for volume in /Volumes/*
    do
        custom_root="$volume/CrossOver/Bottles"
        if [ -d "$custom_root" ]; then
            for candidate in "$custom_root"/*
            do
                resolved_candidate=$(CDPATH='' cd -- "$candidate" 2>/dev/null && pwd -P) || continue
                if [ -f "$resolved_candidate/$GAME_RELATIVE/eldenring.exe" ]; then
                    printf '%s\n' "$resolved_candidate/$GAME_RELATIVE/eldenring.exe" >> "$result_file"
                fi
            done
        fi
    done

    sort -u "$result_file" -o "$result_file"
    count=$(wc -l < "$result_file" | tr -d ' ')
    case "$count" in
        0)
            fail 'No compatible Elden Ring bottle was found. Re-run with --bottle "/full/path/to/bottle".'
            ;;
        1)
            game_executable=$(sed -n '1p' "$result_file")
            ;;
        *)
            printf 'Multiple Elden Ring bottles were found:\n' >&2
            sed 's/^/  /' "$result_file" >&2
            fail 'Choose one with --bottle "/full/path/to/bottle".'
            ;;
    esac

    suffix="/$GAME_RELATIVE/eldenring.exe"
    bottle=${game_executable%"$suffix"}
    rm -f "$result_file"
    trap - EXIT HUP INT TERM
    validate_bottle "$bottle"
}

resolve_bottle()
{
    if [ -n "${BOTTLE_PATH:-}" ]; then
        validate_bottle "$BOTTLE_PATH"
    else
        discover_bottle
    fi
}

require_supported_game()
{
    game_executable="$1/$GAME_RELATIVE/eldenring.exe"
    # The launcher finds its patches by byte pattern, so any game version works as long as the
    # black-bar pattern (74 4F 45 8B 94 CC) occurs exactly once.
    matches=$(perl -0777 -ne 'my $n = () = /\x74\x4F\x45\x8B\x94\xCC/g; print $n' "$game_executable")
    [ "$matches" = 1 ] || fail "The ultrawide patch was not found in this eldenring.exe (matches: $matches). Steam-verify the game files; if that does not help, this game version is not supported yet."
}

ensure_game_closed()
{
    temporary="$1/$GAME_RELATIVE/eldenring_ultrawide_tmp.exe"
    [ ! -e "$temporary" ] || fail 'The temporary executable exists. Close Elden Ring and its launcher before installing or uninstalling.'
}
