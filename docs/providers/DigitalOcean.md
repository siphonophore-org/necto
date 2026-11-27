# DigitalOcean

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **DigitalOcean**, then fill in:

- **Record name**: the record name, such as home.example.com.
- **API token**: a DigitalOcean API token with read and write scope. Create one under API, then Tokens, then Generate New Token.
- **Apex domain**: the apex domain, such as example.com.
- **Server override**: leave blank to use DigitalOcean's own server.

_Ported from ddclient's `nic_digitalocean_update`._

This record can also publish the TXT record Let's Encrypt checks for a [Proxy](../Proxy.md#wildcard-certificates) route's DNS challenge, so no port has to be open and wildcards and local addresses are covered. The API token needs write scope.
