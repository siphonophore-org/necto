# IONOS

Open a record on the **DDNS** page, or select **Add record**, set **Provider** to **IONOS**, then fill in:

- **Host name**: the record name.
- **API Key**: your IONOS API key, in the form prefix.secret. Create it in the IONOS Developer DNS API.
- **TTL (seconds)**: optional, in seconds. `1` uses the provider's default.
- **Server override**: leave blank to use IONOS's own server.

_Ported from ddclient's `nic_ionos_update`._
