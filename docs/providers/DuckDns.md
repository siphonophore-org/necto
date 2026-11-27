# Duck DNS

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **Duck DNS**, then fill in:

- **Labels**: your Duck DNS subdomain label, such as myhost. Separate several with commas, but use one label for a Proxy route. With several labels the record cannot offer its name to a route or publish the TXT record for a DNS challenge.
- **Token**: your Duck DNS token, shown at the top of duckdns.org after you sign in.
- **Server override**: leave blank to use Duck DNS's own server.

This record can also publish the TXT record Let's Encrypt checks for a [Proxy](../Proxy.md#certificates) route's DNS challenge, so no port has to be open and wildcards are covered.

_Ported from ddclient's `nic_duckdns_update`._
