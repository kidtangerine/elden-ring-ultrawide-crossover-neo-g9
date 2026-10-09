# Sharing kit

Use these factual templates only where community rules permit project sharing.
Do not mass-post, misrepresent compatibility, or ask for artificial stars.

## Short post

> Elden Ring on a Samsung Odyssey Neo G9 under CrossOver, edge to edge. I extended
> IgnazioDS's CrossOver ultrawide launcher for 32:9: it fixes the built-in
> resolution list (the reason 3840×1080 still renders 16:9), removes the 16:9
> vignette that darkens the sides, and stops the game relaunching through Steam.
> Tested on an M5 Max / CrossOver 26.3 / Elden Ring 1.17.1:
> https://github.com/kidtangerine/elden-ring-ultrawide-crossover-neo-g9

## Forum or community post

> If you've tried an ultrawide fix for Elden Ring on a 32:9 monitor under
> CrossOver and still got bars (or a wider image with dark sides), these are the
> causes I found on a 57" Neo G9:
>
> 1. The game only renders resolutions from its own 16:9 list, so 3840×1080 in
>    the menu quietly becomes 1920×1080.
> 2. The vignette overlay is sized for 16:9 and blackens the outer thirds.
> 3. Starting the patched exe outside Steam makes it relaunch the stock game
>    through the anti-cheat launcher unless `steam_appid.txt` exists, and it exits at
>    once if Steam isn't running.
>
> This fork of IgnazioDS's launcher handles all three, patches only a temporary
> copy of the game, and uninstalls cleanly:
> https://github.com/kidtangerine/elden-ring-ultrawide-crossover-neo-g9
