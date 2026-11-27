# Sitelutions

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **Sitelutions**, then fill in:

- **Record ID**: the numeric record ID from your Sitelutions Recordbook. It is a number, not a host name, so the record cannot follow the proxy's host names. Type your name yourself as a [Proxy](../Proxy.md#routes) route's **Host name**.
- **Account email**: your Sitelutions account email.
- **Account password**: your Sitelutions password.
- **TTL (seconds)**: optional, in seconds. `1` uses the provider's default.
- **Server override**: leave blank to use Sitelutions's own server.

_Ported from ddclient's `nic_sitelutions_update`._
