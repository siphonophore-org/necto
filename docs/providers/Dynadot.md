# Dynadot

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **Dynadot**, then fill in:

- **Host name**: the host.
- **DDNS password**: your Dynadot DDNS password, from the domain's Dynamic DNS settings in your Dynadot account.
- **Domain**: optional. The domain, such as example.com, used to split the host name into domain and subdomain. Left blank, a bare two-label host name is treated as the apex domain. Fill it in for multi-label subdomains or multi-label TLDs like .co.uk.
- **TTL (seconds)**: optional, in seconds. `1` uses the provider's default.
- **Server override**: leave blank to use Dynadot's own server.

_Ported from ddclient's `nic_dynadot_update`._
