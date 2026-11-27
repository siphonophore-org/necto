# DDNS

**DDNS** keeps each DNS name pointed at this server's public address. It holds several records, each with its own provider, credentials, name, and how often it updates. The public IP lookup is shared by every record.

## The DDNS Tool

The page shows the **Public IPv4** and **Public IPv6** the last run found, then one row per record with its provider, its name, and its last result, such as **Updated**, **Up to date**, **Paused**, or **Failed**. Select a row to open the record in a sidecar, or select **Add record**. **Update now** checks every enabled record at once, whatever its interval, and says how many were updated and how many failed.

A warning shows above the list when a detection service returned only an internal address, and when the server cannot encrypt credentials, in which case no credential can be saved.

## Records

The record sidecar holds the fields its provider uses, with the provider's own labels and a hint under **Provider**.

* **Name** is the record's name in the list. Left blank, the list shows the hostname.
* **Update this record** turns updates on or off. The name stays as it is at the provider while off.
* **Provider** picks one of the providers below.
* **Follow the proxy's host names** pushes every enabled internet route's host name under the record's **Zone**, with the record's credentials, instead of one hostname. A record with no zone follows every internet route. Wildcards and **Local network only** routes are left out. Each followed name keeps its own status, which the sidecar lists under **Last run**.
* **Hostname**, **Login**, **Password**, and **Zone** hold what the provider asks for. A stored credential never reaches the browser. The field shows dots, a blank field keeps the stored value, and **Clear saved value** removes it. A credential sent to another provider or server is dropped, so an edit never sends a saved password somewhere new.
* **Update IPv4** and **Update IPv6** pick the A and AAAA records. Providers that carry one address hide **Update IPv6**.
* **Update every (minutes)** sets how often the record is checked. The update task runs every 5 minutes, which is the shortest interval.
* **More settings** holds **Server override**, **TTL (seconds)**, **Treat as proxied**, and, for the dyndns family, **Wildcard**, **Static DNS**, **MX host**, and **Backup MX**.

**Remove** asks first. The name stays at the provider as it is.

### How a Record Updates

Each run detects the public address, then for each record that is due it compares the address with what DNS serves for the name. A record whose address already matches is skipped, unless **Send again after** says it is time. A record behind a proxy or CDN, with **Treat as proxied** on, compares with the last address pushed instead.

A record that fails **Pause after failures** times in a row pauses for **Pause for**, and a new public address ends the pause early. Timeouts, network errors, rate limits, and provider server errors pause a record only briefly. When a record carries both families and one already matches, only the other is sent.

Provider replies are shown with every credential masked. A request to a provider never follows a redirect to another server.

### Providers

1984 Hosting, ChangeIP, Cloudflare, ClouDNS, DDNS.fm, DDNSS.de, DigitalOcean, Dinahosting, Directnic, DNS Made Easy, DNSExit, Domeneshop, DonDominio, DSLReports, Duck DNS, Dynadot, DynDNS v1, DynDNS v2, Dynu, easyDNS, eNom, FreeDNS, freemyip.com, Gandi LiveDNS, GoDaddy, Hetzner DNS, Hurricane Electric, Infomaniak, INWX, IONOS, Key-Systems, Mythic Beasts, Namecheap, NearlyFreeSpeech.NET, Njalla, No-IP, NS1, OVH, Porkbun, regfish.de, Simply.com, Sitelutions, Spaceship, Yandex PDD, and ZoneEdit.

Cloudflare and Duck DNS can also publish the TXT record Let's Encrypt checks, so a [Proxy](Proxy.md) route can get its certificate by DNS challenge through them.

## Settings

**Settings > DDNS** holds the settings every record shares.

| Setting | Default | What it does |
| :--- | :--- | :--- |
| **IPv4 detection URLs** | ipify, icanhazip, Amazon, ident.me | Services that reply with the IPv4 address, one per line. Two must agree before an address is used, and plain http is refused. |
| **IPv6 detection URLs** | ipify, icanhazip, ident.me | The same for IPv6. |
| **Skip internal addresses** | On | Never publishes a private, loopback, or CGNAT address. |
| **Send again after** | Off | Sends an unchanged address again after this long. |
| **Pause after failures** | 3 failures | Pauses a record after this many failures in a row. Off never pauses. |
| **Pause for** | 24 hours | How long a paused record waits before one more try. |
| **Request timeout (seconds)** | 15 | How long one detection or provider request may take. |

## Troubleshooting

**Troubleshooting > DDNS** shows the addresses the last run found and any detection warning.

* **Update DDNS records** runs the scheduled task, which checks each record that is due.
* **Check every record** checks every enabled record now and lists what each provider said.
* **Start a record over** clears a record's pause, failure count, and last pushed address, so the next run pushes it again.

## Scheduled Task

**Update DDNS records** runs every 5 minutes and at startup. It checks only the records whose interval has passed. Change its schedule under **Dashboard > Scheduled Tasks**.

## Notifications

DDNS sends these events to [Notifications](Plan.md#notifications).

| Event | When |
| :--- | :--- |
| `ddns.failed` | A record could not be pushed, so its name may point at an old address. |
| `ddns.paused` | A record failed often enough to pause. |
| `ddns.recovered` | A record that was failing was pushed again. |
| `ddns.detection_failed` | The public IP could not be found for 4 runs in a row. |

The activity log sums each run into one entry per outcome, such as "DNS records updated", and records every record added, changed, or removed, with who made the change.

## Permissions

DDNS is for administrators only, so it carries no group permission. Which groups receive its events is set on groups under **Notifications**.

## API

Every route needs an administrator.

| Route | What it does |
| :--- | :--- |
| `GET /Ddns/Providers` | Lists the providers with their fields and hint. |
| `GET /Ddns/Records` | Lists the records with their status and the detected addresses. Credentials come back as a marker. |
| `POST /Ddns/Records` | Adds a record. |
| `PUT /Ddns/Records/{id}` | Saves a record. |
| `DELETE /Ddns/Records/{id}` | Removes a record. |
| `POST /Ddns/Records/{id}/Reset` | Starts a record over. |
| `GET /Ddns/Settings` and `POST /Ddns/Settings` | Read and save the shared settings. |
| `POST /Ddns/Update` | Checks every enabled record now. |

Records and their credentials change only through these routes. A save of the whole plugin configuration keeps them as they are.
