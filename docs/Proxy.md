# Proxy

**Proxy** is a reverse proxy that runs inside Jellyfin and forwards each host name to its service, this Jellyfin or any other on your network. A route is a host name, an upstream address, a certificate, and its protection. [Getting Started](GettingStarted.md) walks through the route templates.

## The Proxy Tool

The page opens with notices that need a look, such as a port another program holds, a name that points elsewhere, or visitors that all look local. The cards under them show whether the proxy runs, how many routes it answers for, whether Jellyfin trusts it, and whether the first internet route's name points at this server.

| Proxy | Proxy on a phone |
| :---: | :---: |
| ![Proxy](screenshots/Proxy.png) | ![Proxy on a phone](screenshots/Proxy%20Phone.png) |

The proxy is off until **Settings > Proxy > Enable proxy** is on. While it is off the page says so.

The page reads the proxy's status and routes again every 10 seconds, and the visitors every 30 seconds, so a certificate being issued, Jellyfin restarting, and new visitors or bans show without a reload.

## Routes

Each row shows the route's host name, where it forwards, and its certificate, such as **HTTPS ready**, **Expires soon**, or **Certificate failed**, with **Local only** and **Off** where they apply. Select a row to open the route in a sidecar. **Add route** starts from a template: **Jellyfin**, **Jellyfin LAN**, or **Blank**.

![Route editor](screenshots/Route%20Editor.png)

* **Host name** is the name visitors use, such as `media.example.com`, or a one label wildcard such as `*.example.com`. An exact name beats a wildcard. **Use a DDNS name** fills in a name a DDNS record keeps.
* **Answer for this route** turns the route on or off.
* **Forward to** picks **This Jellyfin** or **Another service**. **Service address** is the other service's `http` or `https` address and port, such as `http://192.168.1.20:8096`. Paths are forwarded as they come.
* **Local network only** answers only the local network and **Trusted networks**. The internet gets nothing.

**Remove** asks first. The route's certificate files are deleted, and the service it forwarded to stays.

A local client that asks by this server's address, or by a name no route has, reaches the first Jellyfin route. The internet must ask for a route's name, and the bare address or a scanner's guess gets nothing. Plain HTTP from the internet is sent on to HTTPS for a route's name once it has a certificate.

## Certificates

Each route has its own certificate, and a handshake is served the one whose name it asked for.

* **Let's Encrypt** issues and renews a certificate. **Challenge** picks how Let's Encrypt checks the name.
    * **HTTP challenge** answers on port 80, so the name must reach this server from the internet.
    * **DNS challenge** publishes a TXT record through a DDNS provider that can, Cloudflare or Duck DNS. It needs no open port and covers wildcards and **Local network only** routes. **DNS record** picks the DDNS record whose credentials publish it, or the record whose zone covers the name.
    * **Pick for me** uses a DNS challenge when a DDNS record covers the name, and an HTTP challenge otherwise.
* **PKCS#12 file** and **PEM files** load a certificate from files on this server and reload it when the files change. **Upload a certificate** takes a `.pfx` with its password, or a PEM chain with its key, and keeps them in the route's folder, readable only by Jellyfin's account.
* **Self-signed** issues a certificate from Necto's own root. **Download root** saves the root, to install once on each device. The root vouches only for the self-signed routes' names and private addresses.
* **No certificate** serves the route over HTTP only.

Nothing is asked of Let's Encrypt until **Settings > Proxy > Accept the Let's Encrypt terms** is on. **Request certificate** orders one now, past the usual timing, and still waits out Let's Encrypt's rate limit. It asks first, and is off until the proxy runs with the terms accepted. After a failure a route backs off before it asks again, and each route keeps its own record of attempts. The sidecar shows what the certificate covers, when it expires, who issued it, and the last attempt's error. A self-signed route also shows when the root expires and its SHA-256 fingerprint.

**Test route** sends one request through the running proxy for the route's name and says whether its service answered.

## Jellyfin

The **Jellyfin** section sets Jellyfin to trust the proxy, so Jellyfin sees each visitor's real address and its own remote access rules and bitrate limits apply. Internet visitors cannot reach a Jellyfin route until it does. Routes to other services do not wait for it.

**Trust the proxy** asks first. It then starts a test copy of the proxy and checks it reaches this Jellyfin, writes Jellyfin's network settings, and restarts Jellyfin once. Forward only ports 80 and 443 to this server, since anyone who reaches Jellyfin's own ports gets past the proxy. **Undo** asks first, then puts them back as they were, and keeps anything an administrator changed since. Jellyfin keeps its own ports throughout, so a proxy that fails to start never locks anyone out. Uninstalling Necto also puts them back.

## Protection

* **Bans** ban an internet address after **Ban after failures** failed Jellyfin sign-ins within **Count failures over (minutes)**, for **Ban length (minutes)**. A ban closes every connection from the address at once, and holds across restarts. Repeat offenders in one IPv6 network are banned by their /56 or /48.
* **Block list** refuses internet addresses and ranges before TLS starts.
* **Rate limits** cap open internet connections per address and in total, requests in flight, sign-ins in flight, upload sizes, and slow readers. The local network is never capped.
* **Locations** refuse internet connections by country with the location database: **This server's country**, **Block these countries**, or **Allow only these countries**, with **Refuse unknown places** and **Refuse without the database**.
* **Trusted networks** are treated like the local network.
* On a Jellyfin route, Jellyfin's startup wizard and API docs are refused from the internet, and its sign-in is read for failed sign-ins.

Every response drops the `Server`, `X-Powered-By`, and timing headers, and gets `nosniff`, a referrer policy, **Same origin frames only**, and **HSTS** for a route's name over HTTPS. Forwarding headers a client sends are replaced, so no client can claim a local address. A save that would block the administrator's own address or country is refused.

## Visitors

The **Visitors** section shows the banned addresses with when each ban began and ends and **Unban**, what each address did with **Block**, **Unblock**, or **Unblock** for its country, and the totals by country with the last visit from each. With **Locations** set to **Country and city**, the activity table also shows each visitor's city.

**Block** and unblocking a country ask first, since a country rule covers many people. Unblocking a country lets its addresses through, and addresses blocked one by one stay blocked. Your network and trusted networks are never blocked, so their **Block** is off. An address blocked by a range in the block list has its **Unblock** off, since the range is changed in **Settings**. Hover a button that is off to see why.

With **Record visitors** off, nothing is recorded. Locations are looked up on this server with DB-IP's free database, which the location task keeps current. The section says when lookups are off.

The [Dashboard](Dashboard.md) map shows the same totals by country, through `GET /Dashboard/Map`, and its share card shows the proxy's public address from `GET /Proxy/PublicAddress`.

## Settings

**Settings > Proxy** holds every setting that is not a route. A field left at its default follows the default, and a field marked **Auto** follows the server's memory.

![Proxy settings](screenshots/settings/Proxy.png)

* **Enable proxy** starts the proxy on its ports. It is off on a fresh install, and while it is off **Proxy** leaves the sidebar, the Dashboard, and the group editor.
* **Accept the Let's Encrypt terms** lets Necto ask Let's Encrypt for certificates.
* **Record visitors**, **Keep history (days)**, and **Locations** set the visitor history.
* **Listener**: **HTTP port** (80), **HTTPS port** (443), **LAN address**, and **HTTP/2**.
* **Jellyfin side**: **Only through the proxy**, which binds Jellyfin to this machine so every client goes through the proxy, and **Trusted networks**. Turning on **Only through the proxy** asks before it saves, since apps, casting, and DLNA that use the server's own address stop working.
* **Security headers**: **HSTS**, **HSTS age (days)**, and **Same origin frames only**.
* **Bans and block list**, **Locations**, **Rate limits**, and **Timeouts** as described above.

Under **Locations**, a note says what the picked country rule does and, for **This server's country**, where this server is. It warns while the location database is missing, when country rules either let everyone through or, with **Refuse without the database**, refuse every internet connection. A line under it counts who the country rules refused since Jellyfin started, by country. **Countries** hides while the rule is **Off**. A note under the **Locations** detail says what each choice costs, and **Country and city** downloads a database of about 130 MB.

## Troubleshooting

**Troubleshooting > Proxy** shows the proxy's state, the addresses it listens on, the Jellyfin it forwards to, the LAN address, the location database, the server's country, and which defaults the server's memory picks. With a public name, it also shows the name, the addresses it points to, and this server's public address.

![Proxy troubleshooting](screenshots/troubleshooting/Proxy.png)

* **Test from outside** sends the first internet route's name to [check-host.net](https://check-host.net), which tries to connect to its HTTPS port from a few countries. Only the name and port are sent.
* **Check from the internet**, **Renew certificates**, and **Update the location database** run their tasks now.
* **Restart the proxy** stops it and starts it again, such as after a port was freed.
* **Reset to defaults** puts every listener and protection value back to its default. It lists the settings that change and asks first. The routes, **Trusted networks**, the block list, and the country rule with its countries stay.
* **Undo trusting the proxy** and **Clear visitor history** ask first.

To turn the proxy off without the dashboard, create an empty file named `DISABLE` in the proxy's folder, `plugins/configurations/Jellyfin.Plugin.Necto.Proxy` in Jellyfin's data folder, and restart Jellyfin. The section shows the full path.

## Scheduled Tasks

| Task | Default schedule | What it does |
| :--- | :--- | :--- |
| **Renew certificates** | Daily at 03:00 | Asks Let's Encrypt for certificates that are missing or within 30 days of expiry, reloads files, and warns two weeks before a certificate expires. |
| **Check from the internet** | Every hour | Runs the outside check and logs when access is lost and when it returns. |
| **Update the location database** | Weekly | Downloads the newest location database. |

## Notifications

| Event | When |
| :--- | :--- |
| `proxy.banned` | An internet address was banned for failed sign-ins. |
| `proxy.certificate_expiring` | A route's certificate expires within two weeks. |
| `proxy.certificate_failed` | A route's certificate could not be issued, renewed, or loaded. |
| `proxy.certificate_issued` | Let's Encrypt issued or renewed a route's certificate. |
| `proxy.start_failed` | The proxy could not start. |
| `proxy.unreachable` | The scheduled check could not reach the HTTPS port from the internet. |
| `proxy.visitors_hidden` | Most visitors look local, so something in front of the proxy hides their addresses. |

## Permissions

Proxy is for administrators only, so it carries no group permission. Which groups receive its events is set on groups under **Notifications**.

## API

Every route needs an administrator.

| Route | What it does |
| :--- | :--- |
| `GET /Proxy/Settings` and `POST /Proxy/Settings` | Read and save the settings. The routes stay as they are. |
| `GET /Proxy/Routes` | Lists the routes and their status. Certificate passwords come back as a marker. |
| `POST /Proxy/Routes`, `PUT /Proxy/Routes/{id}`, `DELETE /Proxy/Routes/{id}` | Add, save, and remove a route. |
| `GET /Proxy/Templates` | Gives the templates under the domain the DDNS records suggest. |
| `POST /Proxy/Routes/{id}/Certificate` | Uploads a certificate, as `multipart/form-data` with `certificate`, `key`, and `password`. |
| `POST /Proxy/Routes/{id}/Certificate/Request` | Orders a Let's Encrypt certificate now. |
| `POST /Proxy/Routes/{id}/Test` | Tests a route through the running proxy. |
| `GET /Proxy/Status` | Describes the proxy. |
| `POST /Proxy/Reachability` | Runs the check from outside. |
| `POST /Proxy/Trust` and `POST /Proxy/Trust/Undo` | Trust the proxy in Jellyfin, and undo it. |
| `POST /Proxy/Restart` | Restarts the proxy. |
| `GET /Proxy/Certificate/Root` | Gives the self-signed root. |
| `GET /Proxy/Defaults` | Gives the defaults in effect. |
| `GET /Proxy/DdnsHostnames` | Lists the names the DDNS records keep. |
| `GET /Proxy/PublicAddress` | Gives the address Jellyfin's apps use, for the share card. |
| `GET /Proxy/Visitors` | Gives the visitors, the bans, the totals by country, this server's location, and the location database status. |
| `POST /Proxy/Visitors/Block`, `Unblock`, `Unban`, `UnblockCountry` | Change the block list. The address or country code is sent in the body as `{ "Value": "203.0.113.5" }`. |
| `POST /Proxy/Visitors/Settings` | Saves the visitor history settings. |
| `POST /Proxy/Visitors/Location/Update` | Downloads the location database now. |
| `DELETE /Proxy/Visitors/History` | Deletes the visitor history. |

Routes and certificate passwords change only through these routes. A save of the whole plugin configuration keeps them as they are.
