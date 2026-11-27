# DDNS Providers

How to add a DNS provider to DDNS and document it, so every provider reads the same way. The providers are ports of ddclient's protocols, so the source of truth for behavior is the matching `nic_{name}_update` in ddclient.

## The Pieces

A provider is one C# class plus two small touch points. Nothing else is wired by hand. `DdnsServices` registers every concrete `IDNSProvider` it finds in the assembly, and a record is matched to a provider by its `Kind`.

| Path | Holds |
| :--- | :--- |
| `Configuration/Ddns/DNSProviderKind.cs` | The enum value for the provider. |
| `Services/Ddns/Providers/IDNSProvider.cs` | The contract, with `Kind`, `Label`, `Hint`, `Fields`, `SupportsIPv6`, and `UpdateAsync`. |
| `Services/Ddns/Providers/IAcmeDnsProvider.cs` | The optional contract for publishing a TXT record for a Let's Encrypt DNS challenge. |
| `Services/Ddns/Providers/DNSProviderBase.cs` | The shared base, with the helpers below. |
| `Services/Ddns/Providers/Implementations/{Kind}Provider.cs` | The provider, with its label, hint, and fields. |
| `docs/providers/{Kind}.md` | The setup guide. |

The record sidecar reads the providers, hints, and fields from `GET /Ddns/Providers`, which is built from each provider class, so `necto_ddns.js` needs no edit per provider.

## Steps

1. Add the value to `DNSProviderKind`. The name is the provider's identity. Name the class `{Kind}Provider` and the guide `{Kind}.md` after it. The guide's name must match exactly, because the **Setup guide** link under **Provider** is built from the value, as `https://necto.siphonophore.org/providers/{Kind}/`.
2. Add `{Kind}Provider.cs` deriving from `DNSProviderBase`. Override `Kind` and implement `UpdateAsync`. Put the request for one address family behind `ApplyPerFamilyAsync`, so IPv4 and IPv6 are handled the same way.
3. Map the record's fields to the provider's credentials in the class summary, and read them from the `DNSRecord` that `UpdateAsync` gets.
4. Override `Label`, `Hint`, and `Fields`. `Label` is the name in the picker, `Hint` the translation key of the note under it, and `Fields` the translation key of each field's label, with null for a field the provider does not use. Add each new key to `Localization/en-us.json` with the `Ddns` prefix. The other `ProviderFields` flags are `Server` for **Server override**, `Ttl` for **TTL (seconds)**, `IPv6` for **Update IPv6**, and `Advanced` for the dyndns options such as `wildcard`.
5. When the protocol sends only IPv4, override `SupportsIPv6` to return false and set `IPv6 = false` in `Fields`, so the toggle hides.
6. When the Hostname field holds a record ID rather than a name, add the kind to `RecordTarget.HoldsRecordId`. That skips the live DNS check, shows the record ID help under **Hostname**, and stops the record from following the proxy. A provider with no Hostname field cannot follow the proxy either. `DdnsController.CanFollowProxy` decides both.
7. When the provider's API can publish and remove TXT records, also implement `IAcmeDnsProvider`. A Proxy route can then use the record for a DNS challenge, with no port 80 open.
8. Add `docs/providers/{Kind}.md` and add it to the list in `docs/providers/index.md`.
9. Add tests under `Jellyfin.Plugin.Necto.Tests/Services/Ddns/Providers/` for the request shape and for how success and failure are read, following the existing provider tests.

## Base Class Helpers

`DNSProviderBase` carries what a provider needs, so each provider stays small.

* `ApplyPerFamilyAsync(record, ip, updater)` runs the updater for each enabled and detected family, then folds the results into one `DNSUpdateResult`. Use it rather than branching on IPv4 and IPv6 by hand.
* `ServerBase(record, defaultServer)` gives the record's **Server override** when set, else the default, so every provider honors it the same way.
* `SendAsync(...)` sends the request through `StolonHttp` and never throws on a network failure. It returns an `HttpResult` with the status and body, and logs a redacted warning on failure.
* `FirstToken(body)` and `FirstLine(body)` read a plain text reply, such as the dyndns family's `good` or `nochg`.
* `Redact(url)` strips credentials from an address before it is logged.
* `ResolveTtl(record)` turns the record's TTL into one the API accepts, with `DefaultTtl` for a TTL of 1.

## The Field Mapping

The class summary is the record of what each field means, so write it precisely. Name the ddclient protocol it ports, then say what `Login`, `Password`, `Zone`, and `Server` map to. The setup guide and the hint come from it.

```csharp
/// <summary>
/// Example, a port of ddclient's <c>nic_example_update</c>. <see cref="DNSRecord.Login"/> is the API key id
/// and <see cref="DNSRecord.Password"/> is the secret. <see cref="DNSRecord.Zone"/> is the domain and
/// <see cref="DNSRecord.Server"/> overrides the default endpoint.
/// </summary>
```

## The Setup Guide

Every provider has a `docs/providers/{Kind}.md`. It opens with the line every guide uses, which names the provider as the picker shows it, then lists only the fields the provider reads, one line each, as `**Field**: where to get the value`. Note any field that is not what its name suggests, such as a Hostname field that takes a record ID. When the provider proxies the record so its public DNS hides the server, like Cloudflare's orange cloud, tell the reader to turn on **Treat as proxied**, so Necto compares with the last address pushed instead of DNS.

## Checklist

* [ ] The kind, the class, and the guide share one name.
* [ ] `Label`, `Hint`, and `Fields` name every field the provider reads, with each key in `en-us.json`.
* [ ] An IPv4 only protocol hides **Update IPv6**.
* [ ] A record ID provider is in `RecordTarget.HoldsRecordId`.
* [ ] The guide is in `docs/providers/` and listed in its index.
* [ ] The provider has tests, and `dotnet test` passes.
