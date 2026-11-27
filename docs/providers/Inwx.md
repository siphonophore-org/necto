# INWX

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **INWX**, then fill in:

- **Username**: your INWX username. A dedicated DynDNS account is recommended.
- **Password**: your INWX password.
- **Server override**: leave blank to use INWX's own server.

INWX works out which record to update from the account. There is no hostname field, so the record saves without one. With no name to look up, Necto compares against the last address it sent. The record cannot follow the proxy's host names, so type your name yourself as a [Proxy](../Proxy.md#routes) route's **Host name**.

_Ported from ddclient's `nic_inwx_update`._
