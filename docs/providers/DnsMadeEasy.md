# DNS Made Easy

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **DNS Made Easy**, then fill in:

- **Record ID**: the numeric ID of the Dynamic DNS record, shown on that record in the DNS Made Easy panel. It is a number, not a host name, so the record cannot follow the proxy's host names. Type your name yourself as a [Proxy](../Proxy.md#routes) route's **Host name**.
- **Account email**: your account email address.
- **Record password**: the dynamic DNS password generated for the record.
- **Server override**: leave blank to use DNS Made Easy's own server.

_Ported from ddclient's `nic_dnsmadeeasy_update`._
