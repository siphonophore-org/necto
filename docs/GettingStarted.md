# Getting Started

This page sets up remote access with **DDNS** and **Proxy**, through the three route templates. Each template only fills in a new route, and every field can be changed after.

## Before You Start

* Remove the Remote Access plugin first. It answers at the same routes, and two plugins on one route break both.
* Own a domain, such as `example.com`, at a DNS provider [DDNS](DDNS.md#providers) supports. A DNS challenge needs Cloudflare, DigitalOcean, Duck DNS, or Porkbun.
* Know your router's port forwarding page. The internet reaches the proxy on ports 80 and 443.

## 1. Keep Your Name Pointed Here

1. Open **DDNS** and select **Add record**.
2. Pick your **Provider**, enter the account details it asks for, and set **Zone** to `example.com`.
3. Turn on **Follow the proxy's host names**, so every internet route you add later is kept pointed at this server.
4. Select **Save**. To send it at once, select **Update every record** in **Troubleshooting > DDNS**. The row shows **Updated** once the provider took the address.

## 2. Turn the Proxy On

1. Open **Settings**, then **Proxy**.
2. Turn on **Enable proxy** and **Accept the Let's Encrypt terms**, and select **Save**.
3. On your router, forward port 80 to this server's **HTTP port** and port 443 to its **HTTPS port**. Both are 80 and 443 unless you change them.

## 3. Add a Route

Open **Proxy** and select **Add route**. The picker suggests your domain from the DDNS record. Pick a template, check **Domain**, and select **Next**. The route's sidecar opens with the template filled in. Select **Save**.

### Jellyfin

`example.com` to this Jellyfin, open to the internet, and `home.example.com` beside it for the local network, on one route with a public certificate.

* **Forward to** is **Jellyfin**, and **Local network only** is off.
* **Local address** is `home.example.com`. Point it at this server's LAN address inside the house, in your router's or your DNS server's local records. Clear it for no local address.
* **Certificate** is **Let's Encrypt** with **Pick for me**, which uses a DNS challenge through a DDNS record that covers both names. Let's Encrypt never reaches a local name, so a local address needs one.
* Turn on **Wildcard certificate** to cover `example.com` and every name under it, so every other route under `example.com` shares the one certificate.

Once saved, the row shows **HTTPS ready** within a minute or two. Then trust the proxy, below, so visitors from the internet get in.

### Blank

An empty route for any other service on your network.

* Enter its **Host name**, such as `app.example.com`.
* **Service address** is the service's address and port, such as `http://192.168.1.30:3000`.
* Pick a certificate, or **No certificate** for HTTP only.

## 4. Trust the Proxy

Open **Proxy**, then the **Jellyfin** section, and select **Trust the proxy**. Necto checks that the proxy reaches this Jellyfin, then sets Jellyfin to trust it and restarts Jellyfin once. Jellyfin then sees each visitor's real address, and internet visitors can reach the Jellyfin routes. **Undo** puts Jellyfin's network settings back.

## 5. Check From Outside

Open **Troubleshooting**, then **Proxy**, and select **Test from outside**. A few places on the internet try the first internet route's HTTPS port, and the Proxy page's **Checks** tab lists which connected. The **Check from the internet** task runs the same check every hour, and logs when access is lost.

## Next Steps

* Set bans, the block list, locations, and rate limits under **Settings > Proxy**. See [Proxy](Proxy.md#protection).
* Pick which groups receive DDNS and Proxy events under **Groups > Notifications**.
