# ClouDNS

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **ClouDNS**, then fill in:

- **Update URL**: the Dynamic URL (DynURL) for the record. In the ClouDNS panel, open the A or AAAA record, turn on Dynamic URL, and paste the full URL here.

The Update URL already names the record. There is no hostname field, so the record saves without one. With no name to look up, Necto compares against the last address it sent. The record cannot follow the proxy's host names, so type your name yourself as a [Proxy](../Proxy.md#routes) route's **Host name**.

_Ported from ddclient's `nic_cloudns_update`._
