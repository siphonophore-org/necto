# Dashboard

**Dashboard** is Necto's home. It shows how each tool that is on stands, where the proxy's visitors and the WireGuard tunnels connect from, the address to share, and what Necto's tools did last.

| Dashboard | Dashboard on a phone |
| :---: | :---: |
| ![Dashboard](screenshots/Dashboard.png) | ![Dashboard on a phone](screenshots/Dashboard%20Phone.png) |

## Cards

A status card sums up each tool that is on, in sidebar order. Select a card to open its tool. **Issues** has a card only with **Enable issues** on, and **Proxy** and **WireGuard** only while each is on. A card whose tool could not be read shows "Error" and "Could not read this tool".

| Card | Number | Chart | Status |
| :--- | :--- | :--- | :--- |
| **Requests** | Requests waiting for approval | A column each for pending, approved, available, and failed requests | **Failed** while any failed, **Pending** while any wait, else **Idle** |
| **Issues** | Open issues, reported or confirmed | A column each for reported, confirmed, resolved, and declined issues | **Open** while any are open, else **Idle** |
| **Invites** | Active invites | One bar split into active, locked, expired, and used up | **Active** while any are active, else **Idle** |
| **Groups** | Groups, besides **Everyone** | None | None. The line says how many users are in a group |
| **Proxy** | Active routes | A dot per route, red for a certificate error | **Running** or **Stopped**, **Failed** with the proxy's error, **Needs attention** when a certificate expires within 14 days, and **Expired** when one has |
| **DDNS** | Records that are on | A dot per record, or per name for a record that follows the proxy, in the color of its last result | **Failed** for a failed record, **Needs attention** for a warning, else **Healthy** |
| **WireGuard** | Connected peers and connections | A dot per peer and connection | **Failed** for a failed tunnel, **Connected** while any is, **Running** while only the host runs, else **Offline** |
| **Scripts** | Runs that failed in the last 14 days | The runs of each day | **Failed** while any failed, **Healthy** with runs, else **Idle** |
| **Pages** | Pages that are on | None | None |
| **Storage** | Disks | A dot per disk in the color the **Storage** tool gives it | **Needs attention** for a disk that is missing, unresponsive, failing, low, or full, else **Healthy** |
| **Notifications** | Sends in the send log | One bar split into sent and failed | **Failed** when the latest send failed, **Needs attention** when an earlier one did, **Healthy** with sends, else **Idle** |

## Locations

**Locations** is one world map with a country table under it. It shows while **Proxy** or **WireGuard** is on, and is hidden while both are off.

| Layer | Shows | Tool |
| :--- | :--- | :--- |
| **Visitors** | The people the proxy saw | Proxy |
| **Failed sign-ins** | Failed sign-ins through the proxy | Proxy |
| **Refused** | Connections the proxy refused | Proxy |
| **Peers** | WireGuard peers, by the address they connect from | WireGuard |
| **Connections** | WireGuard connections, by the host they connect to | WireGuard |
| **This server** | This server's own country | Either |

A tool that is off takes its layers with it. A layer button turns its layer on or off, and the table shows a column for each layer that is on. Select a country on the map to select its row, or a row to mark its country.

Places come from the location database the proxy uses, DB-IP's free database, kept on this server. WireGuard's tunnel ends are placed on the server, and no address leaves it. While WireGuard is on, the database stays loaded at country detail even when **Record visitors** is off. "The location database is loading. Places show once it is ready." shows until it is. The host is placed at this server's country, which Necto looks up from its public address.

## Share Your Server

**Share your server** shows the address people use from the internet as a QR code to scan into the Jellyfin apps, with **Copy image**, **Download**, and **Share** where the browser can share. It shows once the proxy is on with a route to this Jellyfin. **Public** marks a public name. **Home only** marks a name only the home network knows, such as `jellyfin.lan`, and says to add a route with a public name in **Proxy**.

## Recent Activity

**Recent activity** lists Necto's newest activity log entries, newest first, each with its tool's icon. Errors are red and warnings orange. It reads the latest 500 entries of Jellyfin's activity log and lists up to 20 of Necto's.

The map and **Recent activity** are read again every minute while the page is open, and the cards each time it opens.

## Routes

| Route | What it does |
| :--- | :--- |
| `GET /Dashboard/Map` | Gives the map: the proxy's totals by country while it is on, the WireGuard tunnel ends with their countries while it is on, and this server's country |
| `GET /Dashboard/Activity?limit={n}` | Gives Necto's newest activity log entries, up to 50, 20 by default |

The cards read each tool's own routes, such as `GET /Requests/Stats` and `GET /Proxy/Status`, and the share card reads `GET /Proxy/PublicAddress`. Every route is for administrators.
