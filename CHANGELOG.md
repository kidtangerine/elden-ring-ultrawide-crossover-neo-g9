# Changelog

All notable changes to this project are documented here.

## 1.1.1 — 2026-10-22

- Locate every patch by byte pattern at launch instead of fixed offsets, so game
  updates that only move code keep working (fixes the October 2026 update, where the
  icon stopped launching). If the bar pattern is missing, the game starts unpatched.
- Installer no longer requires one exact game version; it checks the patch can be found.

## 1.1.0 — 2026-10-08 (Neo G9 / 32:9 edition)

- Rewrite the 1920×1080 entry of the game's built-in resolution table to the
  configured resolution, so 32:9 modes actually render instead of falling back to 16:9.
- Disable the 16:9 vignette that darkened the sides of wider images.
- Animate NPCs at full rate across the whole screen (they moved in slow motion
  outside the 16:9 area).
- Create `steam_appid.txt` so the patched copy is not relaunched through Steam.
- Start Steam when it is not running and retry while it signs in.
- Detect the screen size at each launch and use it as the resolution; run the game
  unpatched on screens that are not wider than 16:9 (e.g. a laptop on its own).
- Add the `--resolution auto|WIDTHxHEIGHT` installer option (default auto).
- Uninstall also removes `resolution.txt` and `steam_appid.txt`.

## 1.0.0 — 2026-09-15

- Add the verified Elden Ring 1.17.1 temporary ultrawide launcher.
- Add automatic standard and external-volume CrossOver bottle discovery.
- Add checksum and byte-signature validation with fail-closed behavior.
- Add one-command installation, diagnostics, backup, and rollback.
- Preserve the normal CrossOver game-icon launch path.
- Add Windows build and shell-validation CI.

