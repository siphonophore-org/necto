# Directnic

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **Directnic**, then fill in:

- **IPv4 gateway URL**: the IPv4 update URL from the record's dynamic DNS settings at Directnic.
- **IPv6 gateway URL**: the IPv6 update URL from the same place. Leave it blank if you only update IPv4.
- **Server override**: leave blank to use Directnic's own server.

Directnic has no username or password. It issues a gateway URL per record for each address family, and those URLs are all Necto needs. There is no hostname field, so the record saves without one. With no name to look up, Necto compares against the last address it sent. The record cannot follow the proxy's host names, so type your name yourself as a [Proxy](../Proxy.md#routes) route's **Host name**.

_Ported from ddclient's `nic_directnic_update`._
