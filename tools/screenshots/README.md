# Screenshot Kit

Takes every image in `docs/screenshots/`. It runs a throwaway Jellyfin in Docker with the plugin built from this checkout, seeds demo data through Necto's own routes, and drives headless Chromium against it.

The demo server is Living Room, run by `alex`. Its people are `jordan`, `sam`, `riley`, `casey`, and `morgan`, in the Family, Friends, and Kids groups. Every address is under `example.com`, and the server's public address is `203.0.113.24`, from the range kept for documentation.

| File | Does |
| :--- | :--- |
| `server.py` | Builds the plugin, starts the `necto-shots` container on a free port from 8190 up, runs the startup wizard, seeds the data, and removes it all again |
| `seed.py` | The demo data: films, users, groups, invites, DDNS records, proxy routes, WireGuard peers, scripts, custom pages, notification channels, requests, and issues |
| `shoot.mjs` | Opens one page in Chromium at 2x, signed in, with the dashboard header hidden. Pages open at `http://media.example.com`, which it sends to the kit's Jellyfin, so every address they show is the demo one |
| `shots.mjs` | Sets up each subject and writes its PNG to `docs/screenshots/` |

## Requirements

* Docker, Python 3, Node, and the dotnet SDK.
* The `jellyfin/jellyfin:12.4` image. Set `JELLYFIN_IMAGE` to use another.
* playwright-core. Set `PLAYWRIGHT_CORE` to its `index.mjs` unless Node resolves `playwright-core` on its own.
* Chromium. The default is Playwright's `chromium-1148` in `~/Library/Caches/ms-playwright`. Set `CHROMIUM` to use another binary.
* `NUGET_CONFIG` when Stolon comes from a local package, as `build.sh` reads it.

## Running

Start the server from this folder. It never uses ports 8096 to 8098.

```bash
python3 -I server.py up
```

Take every shot.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shots.mjs
```

Take only the named shots. A name is the file's path under `docs/screenshots/` without `.png`.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shots.mjs "Proxy,settings/Proxy"
```

Remove the container and its state.

```bash
python3 -I server.py down
```

Each shot prints `ok`, or the errors the page logged. Look at every PNG it writes before committing it. Dates and times in the shots are the day the kit ran.

## Notes

* `seed` seeds again on a running server, for data added to `seed.py`.
* The kit points DDNS's address lookups at `example.com` before Jellyfin first starts, and writes the DDNS status with the demo address while Jellyfin is stopped, so the real public address never shows.
* Each WireGuard peer brings its own public key, since the kit does not download wireguard-go.
* Proxy routes use self-signed certificates, so no certificate authority is asked about `example.com`.
* Notification channels are added last, after the demo events. Their made up addresses fail any later send.
* The live updates socket cannot follow the demo address, so its errors are left out of what each shot prints.
* The sign-up, members' invites, and custom page shots open as `jordan`, so the web client's header shows. The sign-up shot keeps its form empty.
* A new subject is one more entry in the `shots` list of `shots.mjs`. New data goes in `seed.py`.
