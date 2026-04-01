# OpenRA

A Libre/Free Real Time Strategy game engine supporting early Westwood classics.

* Website: [https://www.openra.net](https://www.openra.net)
* Chat: [#openra on Libera](ircs://irc.libera.chat:6697/openra) ([web](https://web.libera.chat/#openra)) or [Discord](https://discord.openra.net) ![Discord Badge](https://discordapp.com/api/guilds/153649279762694144/widget.png)
* Repository: [https://github.com/OpenRA/OpenRA](https://github.com/OpenRA/OpenRA) ![Continuous Integration](https://github.com/OpenRA/OpenRA/workflows/Continuous%20Integration/badge.svg)

Please read the [FAQ](https://github.com/OpenRA/OpenRA/wiki/FAQ) in our [Wiki](https://github.com/OpenRA/OpenRA/wiki) and report problems at [https://github.com/OpenRA/OpenRA/issues](https://github.com/OpenRA/OpenRA/issues).

Join the [Forum](https://forum.openra.net/) for discussion.

## Play

Distributed mods include a reimagining of

* Command & Conquer: Red Alert
* Command & Conquer: Tiberian Dawn
* Dune 2000

EA has not endorsed and does not support this product.

Check our [Playing the Game](https://github.com/OpenRA/OpenRA/wiki/Playing-the-game) Guide to win multiplayer matches.

## Contribute

* Please read [INSTALL.md](https://github.com/OpenRA/OpenRA/blob/bleed/INSTALL.md) and [Compiling](https://github.com/OpenRA/OpenRA/wiki/Compiling) on how to set up an OpenRA development environment.
* See [Hacking](https://github.com/OpenRA/OpenRA/wiki/Hacking) for a (now very outdated) overview of the engine.
* Read and follow our [Code of Conduct](https://github.com/OpenRA/OpenRA/blob/bleed/CODE_OF_CONDUCT.md).
* To get your patches merged, please adhere to the [Contributing](https://github.com/OpenRA/OpenRA/blob/bleed/CONTRIBUTING.md) guidelines.

## Mapping

* We offer a [Mapping](https://github.com/OpenRA/OpenRA/wiki/Mapping) Tutorial as you can change gameplay drastically with custom rules.
* For scripted mission have a look at the [Lua API](https://docs.openra.net/en/latest/release/lua/).
* If you want to share your maps with the community, upload them at the [OpenRA Resource Center](https://resource.openra.net).

## Modding

* Download a copy of the [OpenRA Mod SDK](https://github.com/OpenRA/OpenRAModSDK) to start your own mod.
* Check the [Modding Guide](https://github.com/OpenRA/OpenRA/wiki/Modding-Guide) to create your own classic RTS.
* There exists an auto-generated [Trait documentation](https://docs.openra.net/en/latest/release/traits/) to get started with yaml files.
* Some hints on how to create new OpenRA compatible [Pixelart](https://github.com/OpenRA/OpenRA/wiki/Pixelart).
* Upload total conversions at [our Mod DB profile](https://www.moddb.com/games/openra/mods).

## Support

* Sponsor a [mirror server](https://github.com/OpenRA/OpenRAWebsiteV3/tree/master/packages) if you have some bandwidth to spare.
* You can immediately set up a [Dedicated](https://github.com/OpenRA/OpenRA/wiki/Dedicated-Server) Game Server.

## Browser multiplayer (this fork)

Architecture and scope: [docs/browser-multiplayer/01-architecture-and-scope.md](docs/browser-multiplayer/01-architecture-and-scope.md). **Setup (dedicated + proxy + browser):** [docs/browser-multiplayer/04-configuracion-multijugador-desde-el-navegador.md](docs/browser-multiplayer/04-configuracion-multijugador-desde-el-navegador.md). Fast path in one terminal: `./run-browser-multiplayer.sh` (incremental build + dedicated + tunnel + browser host + join URL; add `--open` on macOS to open the browser). Manual scripts in root remain available: `build-browser-multiplayer-local.sh` → `run-dedicated-server.sh` → `run-browser-tunnel-proxy.sh` → `run-browser-host.sh` → `print-browser-multiplayer-join-url.sh`. Tunnel proxy: [tools/OpenRA.BrowserTunnelProxy](tools/OpenRA.BrowserTunnelProxy) — see [02-proxy-and-infrastructure.md](docs/browser-multiplayer/02-proxy-and-infrastructure.md) for `LISTEN`, `TLS_CERT`/`TLS_KEY`, `MAX_CONCURRENT`, `IDLE_TIMEOUT`, `RATE_LIMIT_PER_MINUTE`, `EDGE_TOKEN`, and `TOKEN_ROUTES`. The Blazor host accepts `?tunnel=ws://…` or `?tunnel=wss://…` and passes it as `Browser.TunnelUrl=` for auto-join after load. Default `Game.Mod` is **ra** (matches `copy-ra-content-to-browser.sh` / `wwwroot/support/Content/ra/v2`); use `?mod=cnc` and `copy-cnc-content-to-browser.sh` for Tiberian Dawn. `./run-dedicated-server.sh` defaults to `Game.Mod=ra` like the browser host; pass `Game.Mod=cnc` to override. Rebuilds with `OpenRaBrowserBuild=false` so `./bin/` matches desktop after Wasm builds. Sets `Engine.EngineDir=..` so `./bin/` finds `mods/`.

### Game content (browser) — not in this repository

Desktop OpenRA downloads or locates original-game assets into the user support directory; the web build cannot run that installer inside Wasm, so the same files must be available over HTTP. **Do not commit them:** `OpenRA.BrowserHost/wwwroot/support/` is listed in `.gitignore`. **Without desktop OpenRA:** [`./fetch-browser-game-content.sh`](fetch-browser-game-content.sh) downloads the same official freeware ZIPs as the content installer (`quickinstall` for RA, `basefiles` for C&C TD) into `OpenRA.BrowserHost/wwwroot/support/`. [`./ensure-browser-game-content.sh`](ensure-browser-game-content.sh) only fetches if marker files are missing — it is run automatically by `run-browser-host.sh`, `run-browser-multiplayer.sh`, and `run-browser-multiplayer-ngrok.sh` (for `ra` / `cnc`). Set `SKIP_BROWSER_GAME_CONTENT=1` to skip that step. **With desktop OpenRA:** `./copy-ra-content-to-browser.sh` and/or `./copy-cnc-content-to-browser.sh` (or copy by hand). CI/production: build and publish the Wasm app without those binaries, then deploy `support/Content/…` alongside the site or upload it to object storage and long-cache it (see [OpenRA.BrowserHost/HTTP-CACHE-NOTES.txt](OpenRA.BrowserHost/HTTP-CACHE-NOTES.txt)). To load mixes from another origin (CDN), open the app with an absolute URL query parameter, for example `?supportOrigin=https%3A%2F%2Fcdn.example.com%2F` — the CDN must expose the same path layout (`support/Content/ra/v2/…` or `support/Content/cnc/…`) and send `Access-Control-Allow-Origin` for your app’s origin when it differs from the game page.

## License
Copyright (c) OpenRA Developers and Contributors
This file is part of OpenRA, which is free software. It is made
available to you under the terms of the GNU General Public License
as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version. For more
information, see [COPYING](https://github.com/OpenRA/OpenRA/blob/bleed/COPYING).
