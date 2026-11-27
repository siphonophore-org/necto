# Porkbun

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **Porkbun**, then fill in:

- **Host name**: the record.
- **API Key**: your Porkbun API key, starting with pk1_.
- **Secret API key**: your Porkbun secret API key, starting with sk1_. Create both at porkbun.com/account/api and turn on API access for the domain.
- **Root domain**: optional. The root domain, such as example.com. Left blank, the host name is split on its first dot, and a bare two-label host name is treated as the apex domain. Fill it in for multi-label subdomains or multi-label TLDs like .co.uk.
- **Server override**: leave blank to use Porkbun's own server.

_Ported from ddclient's `nic_porkbun_update`._

This record can also publish the TXT record Let's Encrypt checks for a [Proxy](../Proxy.md#wildcard-certificates) route's DNS challenge, so no port has to be open and wildcards and local addresses are covered. Fill in **Root domain**, and turn on API access for the domain in Porkbun.
