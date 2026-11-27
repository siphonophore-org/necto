# Necto Plan

Necto is the Siphonophore administration plugin. It brings seven JPKribs plugins into one, built on Stolon, with one place for permissions (groups) and one place for notifications. This page is the build plan, and every decision made without review is marked **Review**.

## Plugins Accounted For

| Plugin | Version | Goes to |
| :--- | :--- | :--- |
| Remote Access | 12.4.5.3 | Necto: **DDNS**, **Proxy**, the location database, the map |
| WireGuard | 12.4.0.0 | Necto: **WireGuard** |
| User Management | 12.4.4.1 | Necto: **Groups**, **Invites**, and the account rules inside Groups |
| Scripts | 12.4.4.1 | Necto: **Scripts** |
| Storage Manager | 12.4.1.1 | Necto: **Storage** |
| Custom Pages | 12.4.4.1 | Necto: **Custom Pages** |
| Requests | 12.4.8.2 | Necto: **Requests** and **Issues** |
| Server Sync | 12.4.21.0 | Zooid, not Necto |
| Artwork Generator | 12.4.8.1 | Stays a JPKribs plugin. **Review** |
| Live Channels | 12.4.5.1 | Stays a JPKribs plugin. **Review** |
| YouTube Audio | 12.4.6.1 | Stays a JPKribs plugin. **Review** |

Necto 12.4.0.0 is a first release. It reads nothing the old plugins stored, so a server installs Necto fresh. The old plugins must be removed first: they answer at the same routes, and two plugins on one route break both (Zooid met this with Server Sync).

## Tools

The page is one Stolon tool app, as Zooid's is: a sidebar of the tools that are on, with **Dashboard** as home, and a title menu on a phone. Each tool has its own address. A tool that is turned off in **Settings** leaves the sidebar.

| Tool | From | Holds |
| :--- | :--- | :--- |
| **Dashboard** | Remote Access, WireGuard, Requests | One map of proxy visitors and WireGuard peers, hidden when Proxy and WireGuard are both off. A status card per tool that is on. Recent activity. |
| **Requests** | Requests | The request list and its detail sidecar, Radarr and Sonarr, discovery and imports. Who may request, the limits, and auto approval live on groups. |
| **Issues** | Requests | Issue reports and their states and notes. Who may report lives on groups. |
| **Invites** | User Management | Invite links and their signup page, the group a new user joins, limits, expiry. |
| **Groups** | User Management | Groups and members, and every permission in Necto. See [Groups](#groups). |
| **Proxy** | Remote Access | A general reverse proxy with routes, certificates, and protection. See [Proxy](#proxy). |
| **DDNS** | Remote Access | Several DDNS records, each with its provider and credentials. |
| **WireGuard** | WireGuard | Host, peers, connections, DNS. Peer counts come from groups. |
| **Scripts** | Scripts | Scripts with triggers, conditions, steps, secrets, variables, history, export and import. Who a script runs for comes from groups. |
| **Custom Pages** | Custom Pages | Pages, assets, stored data, server side routes. Who may open a page comes from groups. |
| **Storage** | Storage Manager | Disks, space, SMART health, missing disks and remounting, formatting, shares, libraries per disk. |
| **Notifications** | Every tool | Channels, and which events go to whom. See [Notifications](#notifications). |
| **Settings** | Every tool | A collapsible section per tool, each with its own subsections. |
| **Troubleshooting** | Every tool | A collapsible section per tool: resets, tests, logs, repairs. |

### Groups

Groups are the only place a permission is set. A tool's own page sets how the tool works, never who may use it.

* A user may be in several groups. An allow from any group allows, and a count or limit takes the largest any group gives. **Review**
* What a group carries:
  * **Requests:** may request, may auto approve, quotas and limits by type, may see others' requests, may manage requests.
  * **Issues:** may report, may manage issues.
  * **Custom pages:** which pages members may open.
  * **Scripts:** which scripts run for members.
  * **WireGuard:** may have peers, how many, and limits.
  * **Invites:** may create invites, how many, into which groups.
  * **Accounts** (from User Management): password rules, password resets, expiration, inactivity, session rules.
  * **Notifications:** which events members receive, and on which channels.
* Users outside every group take the **Everyone** group, which always exists. Everyone never adds to a real group's permissions.
* A count or limit of 0 is no limit, so it is the largest. A limit counts only from groups that allow the action. **Review**
* Account rules combine the other way, since any one group may keep an account. Password rules hold when any group enforces them, at their strictest, with the most generous change mode. Expiry, inactivity, and session cleanup hold only when every group has them, at the largest window, and expiry deletes only when every group asks for it. **Review**
* User Management's default group is **Settings > Groups > Group for new users**. Existing users in no group stay in **Everyone** rather than being moved into it. **Review**
* User Management's Jellyfin policy and preference templates (**Access and features**, **Preferences**) sit on groups. A member takes the template of the first of their groups, in group order, that has one on, since a whole policy cannot be merged field by field. Groups have an order the Groups tool changes, and Everyone stays first. **Review**
* A template is written onto every user after any group save or reorder, and when the server starts, as User Management's sync did. **Review**
* The request quota window is a Requests setting, and the quotas by type are permissions. **Review**
* See [Groups](Groups.md) for what is built.

### Proxy

The proxy forwards to any service, not only Jellyfin. A route is a host name, an upstream address, a certificate, and its protection.

* **Templates** for a new route:
  * **Jellyfin:** `domain.ext` to this Jellyfin, open to the internet, with a public certificate.
  * **Jellyfin LAN:** `home.domain.ext` to this Jellyfin, answered only from the local network, with a certificate by DNS challenge so HTTPS works inside the house.
  * **Blank:** an empty route for any other service.
* **Certificates:** ACME by HTTP or DNS challenge, using the DDNS providers' credentials for DNS, or an uploaded certificate. Renewal is a scheduled task.
* **Protection:** bans, rate limits, locations, trusted proxies, and the Jellyfin side settings Remote Access checked.
* Dropped: automatic port forwarding (UPnP and NAT-PMP), router steps, and the step by step setup. The docs carry a Getting Started page for the templates instead.

### DDNS

Several records, each with its own provider, credentials, name, and how often it updates. The public IP lookup is shared. A record may follow the proxy's host names.

### WireGuard

Turning WireGuard on asks to download `wireguard-go`, which is the only way WireGuard runs. **Settings > WireGuard** shows the download and its version, and replaces it. A server with its own WireGuard uses that instead of Necto.

* The download is `wgsidecar`, the WireGuard plugin's own sidecar on wireguard-go, built from `sidecar/` by the **Release wgsidecar** workflow and published as the release `wgsidecar-<version>` of this repository. Necto checks it against the release's `SHA256SUMS`, which catches a damaged download but not a release that was itself replaced. The workflow must run before WireGuard can be turned on. **Review**
* A server with its own WireGuard leaves **Enable WireGuard** off. Necto has no mode that drives a system WireGuard. **Review**
* The host's key pair is made the first time the host runs with wireguard-go downloaded, and a peer or connection that needs a new key pair is refused until then. A save from **Settings** keeps the stored peers, connections, and host keys, which only the WireGuard routes change. **Review**
* **Data per user** moves onto groups as **Data per member (GB)** with its **Period**. Several groups give the largest limit of the groups that allow peers, with that group's period. **Review**
* Groups decide which peers connect. A peer whose owner has no group with **Allow peers** is left out of the tunnel, and an owner past **Peers per member** keeps their oldest enabled peers. Administrators' peers and peers with no owner always connect, and administrators add peers for any user, as the WireGuard plugin let them. **Review**
* The location lookups, the **Location** setting, and the **Refresh WireGuard locations** task are not in WireGuard. `GET /WireGuard/Peers` gives each tunnel end's public address, and the Dashboard map places it with the location database from Remote Access. **Review**
* Peers connecting, connections, data limits, and failing tunnels are written to the activity log as the WireGuard plugin wrote them, and each is also sent to Notifications. **Review**
* The keys and the script secrets share one secret protector, named `Jellyfin.Plugin.Necto`. **Review**

### Scripts

Everything the Scripts plugin does, with its history in **Troubleshooting** and its settings in **Settings**.

* A script runs for a user only when one of the user's groups runs it, by **Run every script** or **Scripts that run**. Everyone runs no script on a fresh install, where the Scripts plugin's empty users list ran a script for everyone. **Review**
* A failed sign in for a name with no account takes **Everyone**. **Review**
* `scripts.failed` goes to Notifications at most once a minute for each script, so a burst of failures sends one alert. **Review**
* An export is marked `necto-scripts`, and import reads a Scripts plugin export too. A script's users are not carried over, since groups decide them.
### Custom Pages

Everything Custom Pages does. A page keeps its tier, **Anyone**, **Signed in**, or **Administrators**, and its list of named users is gone, since groups give pages.

* A **Signed in** page opens for administrators and for members of a group that gives it, with **Open every page** or by name. **Everyone** gives no page on a fresh install, so a page opens for nobody but administrators until a group gives it. **Review**
* An API key opens **Administrators** pages, as the elevation policy lets it, but no **Signed in** page, since it is in no group. **Review**
* Stores keep their **Read** and **Write** tiers, **Scope**, and **Writers edit own records** on the store. They name classes of caller, not users, and a store has no group section yet. **Review**
* The pages keep their own addresses under `/pages`, since served pages and their helpers call them. The administrator routes are under `/Pages` with two segments or more, and the slugs `asset`, `store`, `stores`, and `configuration` are kept for them. **Review**
* A server route never follows a redirect, so its credentials stay with the host it names. **Review**
* **Settings > Custom pages** holds the record limit, retention, and notify a new store starts with, the only settings that are not on a page, image, or store. **Clear** for a store is in **Troubleshooting > Custom pages**. **Review**
* Store writes go to Notifications as `pages.*` events, summed per burst like the activity log entries. **Review**

### Storage

Everything Storage Manager does. Pooling disks or RAID is left out of 12.4.0.0: it needs the operating system's own tools run as root, which a plugin cannot do safely, and a pool Jellyfin cannot see as one disk gains nothing over a library with several folders. **Review**

* Disk alerts write the activity log as Storage Manager did, and also go to Notifications under `storage.*` keys. Once the activity log is a Notifications channel, one of the two may need to go so entries are not written twice. **Review**
* A disk's own alerts and its share address stay in the disk's sidecar on the **Storage** page, since each belongs to one disk the check found. Everything else is in **Settings > Storage**. **Review**
* Formatting, erasing, and remounting keep Storage Manager's safeguards: a typed confirm, a second look at the disk when the format starts, one format at a time, and no format for the system disk, a mounted or stacked disk, or a library's own disk. **Review**
* **Troubleshooting > Storage** can put every Storage setting back to its default after a confirm, each disk's own alerts and share addresses with it. **Review**

### Notifications

One service every tool sends events to, and one set of channels.

* **Channels:** browser push, email, Discord, webhooks, and Jellyfin's activity log.
* **Events:** new and changed requests, issue reports and updates, invite use, proxy bans and certificate expiry, WireGuard connections, script failures, disk alerts and missing disks, DDNS failures.
* Who receives an event is set on groups. Administrators may receive everything.
* User pages (Requests, Issues, Invites, custom pages) carry one service worker and one push subscription. The worker is `Notifications/Push/Worker.js`, registered at the server's root with `Service-Worker-Allowed`, so one subscription serves every user page. It has no fetch handler, so it never answers for Jellyfin's own pages. **Review**
* Requests' Jellyfin messages to the requester and to administrators are kept: **Jellyfin messages** is a user channel, and the **Administrators** channel sends to every administrator's apps and browsers. **Review**
* **Administrators** takes every event and **Activity log** none by default, since each tool already writes its own activity entries. **Review**
* Email is sent with .NET's SMTP client and STARTTLS. A server that takes only TLS from the start, on port 465, cannot be used. **Review**
* The send log keeps the latest 200 sends in memory. **Review**
* The Notifications section of Settings saves through `/Notifications/Settings`, so stored addresses, headers, and the SMTP password never reach the page. **Review**
* A tool writes its own event's words. Requests' per update wording and post fields move with Requests, and every channel's **Use a custom body** covers a custom format. **Review**
* See [Notifications](Notifications.md) for what is built.

### Invites

* Invites keep User Management's links, PINs, limits, expiry, and signup page, at `/invites/{token}`. See [Invites](Invites.md).
* A member whose groups allow invites makes their own at `/invites`, the user page the Plan names, so **Allow invites**, **Open invites**, and the invite groups take effect. **Review**
* The Invites tool lists invites at `GET /Invites/List`, since `/invites` is the member page and routes do not tell case apart. **Review**
* An invite's group of **Group for new users** follows **Settings > Groups** when the link is used, as User Management's default group did. **Review**
* A signup holds the account disabled until its password is set, leaves it out of the group for new users, and counts its first password as a creation, not a member's own change.

## Routes

Every route says what it does, as Zooid's do: `/Requests`, `/Issues`, `/Invites`, `/Groups`, `/Proxy`, `/Ddns`, `/WireGuard`, `/Scripts`, `/Pages`, `/Storage`, `/Notifications`, and `/Necto` only for the page's own strings. None of them is a route of Jellyfin 12.4 itself. The user pages are `/requests`, `/issues`, `/invites`, and the custom pages' own addresses.

## Settings and Troubleshooting

**Settings** holds every setting that is not a permission, in one collapsible section per tool and subsections inside it. **Troubleshooting** holds the resets, tests, and repairs in the same sections. No tool page carries its own settings tab.

## Build Order

1. **Scaffold.** The repository as Zooid's is laid out: build, tests, docs, release and unstable workflows, the tool app, translations, the brand images.
2. **Core.** Groups and the permission check every tool calls, Notifications and its channels, Settings and Troubleshooting sections, the Dashboard's cards.
3. **Groups and Invites,** with the account rules.
4. **DDNS.**
5. **Proxy,** with certificates and protection.
6. **WireGuard,** with the download.
7. **Scripts.**
8. **Custom Pages.**
9. **Storage.**
10. **Requests and Issues,** with their user pages.
11. **Dashboard** map and cards, then docs and screenshots for every tool.

Each step ports the old plugin's behavior and tests, moves its permissions onto groups and its alerts onto Notifications, and lands with its docs.
