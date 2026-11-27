using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Ddns;

/// <summary>
/// The routes of the DDNS tool: the providers, the records with their status, the shared settings, and an update
/// run now. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Ddns")]
[Produces(MediaTypeNames.Application.Json)]
public class DdnsController : ControllerBase
{
    private readonly DNSUpdateService _updateService;
    private readonly StatusStoreService _statusStore;
    private readonly SecretProtector _secrets;
    private readonly ActivityLogger _activity;
    private readonly IEnumerable<IDNSProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="DdnsController"/> class.
    /// </summary>
    /// <param name="updateService">Runs the update.</param>
    /// <param name="statusStore">Holds each record's status.</param>
    /// <param name="secrets">Encrypts credentials at rest.</param>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="providers">The DNS providers, which drive the provider list and its fields.</param>
    public DdnsController(DNSUpdateService updateService, StatusStoreService statusStore, SecretProtector secrets, ActivityLogger activity, IEnumerable<IDNSProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(statusStore);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(providers);
        _updateService = updateService;
        _statusStore = statusStore;
        _secrets = secrets;
        _activity = activity;
        _providers = providers;
    }

    /// <summary>
    /// Lists every provider with its name, hint, fields, and whether it can publish a TXT record for a DNS challenge.
    /// </summary>
    /// <returns>The providers sorted by name.</returns>
    [HttpGet("Providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<object>> GetProviders()
    {
        var language = ServerText.LanguageOf(Request);
        return Ok(_providers
            .OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                Value = p.Kind.ToString(),
                Label = p.Label,
                Hint = StolonTranslations.Get(p.Hint, language),
                Fields = p.Fields.Translated(language),
                Txt = p is IAcmeDnsProvider,
                Follow = CanFollowProxy(p)
            })
            .ToList());
    }

    /// <summary>
    /// Lists the records with stored credentials replaced by a marker and each record's status, with the public
    /// addresses the last run found.
    /// </summary>
    /// <returns>The records and the detected addresses.</returns>
    [HttpGet("Records")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> ListRecords()
    {
        var status = _statusStore.Load();
        var (records, proxy) = Plugin.Instance?.ReadConfiguration(c => (c.Ddns.Records.Select(r => r.Clone()).ToList(), ProxyRuntimeFactory.Clone(c.Proxy)))
            ?? (new List<DNSRecord>(), new ProxySettings());
        var hosts = RecordExpansion.HostsOf(proxy);
        return Ok(new
        {
            Records = records.Select(r => new
            {
                Record = Redact(r, status.Records.GetValueOrDefault(r.Id)),
                Followed = r.FollowProxyHosts
                    ? RecordExpansion.Covered(r, hosts).Select(h => Redact(Followed(r, h), status.Records.GetValueOrDefault(r.Id + RecordExpansion.Separator + h))).ToList()
                    : new List<DNSRecord>()
            }).ToList(),
            DetectedIPv4 = status.DetectedIPv4,
            DetectedIPv6 = status.DetectedIPv6,
            DetectionMessage = status.DetectionMessage,
            SecretsPlaintext = !_secrets.IsAvailable
        });
    }

    /// <summary>
    /// Adds a record. Typed credentials are encrypted before they are saved.
    /// </summary>
    /// <param name="record">The new record. Any id it carries is replaced.</param>
    /// <returns>The record as saved, or 400 with the reason.</returns>
    [HttpPost("Records")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<DNSRecord> CreateRecord([FromBody] DNSRecord record)
    {
        if (record is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ErrorNothingSent)));
        }

        record.Id = Guid.NewGuid().ToString("N");
        return Save(record, null);
    }

    /// <summary>
    /// Saves a record. A credential sent as the kept marker keeps its stored value while the record still goes to
    /// the same provider and server, and an empty one removes it.
    /// </summary>
    /// <param name="id">The record's id.</param>
    /// <param name="record">The record's new values.</param>
    /// <returns>The record as saved, 400 with the reason, or 404 when no record has the id.</returns>
    [HttpPut("Records/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DNSRecord> UpdateRecord([FromRoute] string id, [FromBody] DNSRecord record)
    {
        if (record is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ErrorNothingSent)));
        }

        var existing = Plugin.Instance?.ReadConfiguration(c => c.Ddns.Records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal))?.Clone());
        if (existing is null)
        {
            return NotFound(Errors(Reply(TranslationKeys.DdnsErrorRecordNotFound)));
        }

        record.Id = existing.Id;
        return Save(record, existing);
    }

    /// <summary>
    /// Removes a record. Its DNS name stays at the provider as it is.
    /// </summary>
    /// <param name="id">The record's id.</param>
    /// <returns>204, or 404 when no record has the id.</returns>
    [HttpDelete("Records/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RemoveRecord([FromRoute] string id)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        List<DNSRecord>? before = null;
        List<DNSRecord>? after = null;
        plugin.MutateConfiguration(c =>
        {
            before = c.Ddns.Records;
            after = c.Ddns.Records.Where(r => !string.Equals(r.Id, id, StringComparison.Ordinal)).ToList();
            if (after.Count == before.Count)
            {
                return false;
            }

            c.Ddns.Records = after;
            return true;
        });

        if (before is null || after is null || after.Count == before.Count)
        {
            return NotFound(Errors(Reply(TranslationKeys.DdnsErrorRecordNotFound)));
        }

        ConfigurationAudit.LogRecordChanges(_activity, before, after, ActivityUser.Of(User), ActivityUser.NameOf(User));
        _statusStore.RemoveRecords(_statusStore.Load().Records.Keys.Where(k => string.Equals(RecordExpansion.SourceId(k), id, StringComparison.Ordinal)));
        return NoContent();
    }

    /// <summary>
    /// Gives the shared settings: the public IP lookup, refreshes, backoff, and the request timeout.
    /// </summary>
    /// <returns>The settings, without the records.</returns>
    [HttpGet("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DdnsSettings> GetSettings()
        => Ok(Plugin.Instance?.ReadConfiguration(c => SettingsOnly(c.Ddns)) ?? new DdnsSettings());

    /// <summary>
    /// Saves the shared settings. The records stay as they are.
    /// </summary>
    /// <param name="incoming">The settings. Its records are ignored.</param>
    /// <returns>The settings as saved, or 400 with the reason.</returns>
    [HttpPost("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<DdnsSettings> SaveSettings([FromBody] DdnsSettings incoming)
    {
        var plugin = Plugin.Instance;
        if (incoming is null || plugin is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ErrorNothingSent)));
        }

        incoming.Records = new List<DNSRecord>();
        DdnsLimits.Clamp(incoming);
        if (XmlText.FindInvalid(incoming) is { } bad)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ErrorInvalidCharacter, bad)));
        }

        var changed = false;
        plugin.MutateConfiguration(c =>
        {
            var d = c.Ddns;
            changed = d.IPv4DetectionUrl != incoming.IPv4DetectionUrl
                || d.IPv6DetectionUrl != incoming.IPv6DetectionUrl
                || d.ForceUpdateHours != incoming.ForceUpdateHours
                || d.SkipInternalAddresses != incoming.SkipInternalAddresses
                || d.BackoffAfterFailures != incoming.BackoffAfterFailures
                || d.BackoffHours != incoming.BackoffHours
                || d.RequestTimeoutSeconds != incoming.RequestTimeoutSeconds;
            d.IPv4DetectionUrl = incoming.IPv4DetectionUrl;
            d.IPv6DetectionUrl = incoming.IPv6DetectionUrl;
            d.ForceUpdateHours = incoming.ForceUpdateHours;
            d.SkipInternalAddresses = incoming.SkipInternalAddresses;
            d.BackoffAfterFailures = incoming.BackoffAfterFailures;
            d.BackoffHours = incoming.BackoffHours;
            d.RequestTimeoutSeconds = incoming.RequestTimeoutSeconds;
            return changed;
        });

        if (changed)
        {
            _activity.Log(ServerText.Get(TranslationKeys.DdnsActivityDynamicDnsSettingsChanged), "Necto.Ddns.SettingsChanged", ServerText.Format(TranslationKeys.DdnsActivityDynamicDnsSettingsChangedOverview, ActivityUser.NameOf(User)), userId: ActivityUser.Of(User));
        }

        return Ok(plugin.ReadConfiguration(c => SettingsOnly(c.Ddns)));
    }

    /// <summary>
    /// Checks every enabled record now, whatever its update interval. A record whose address is unchanged is
    /// skipped as on a scheduled run.
    /// </summary>
    /// <remarks>
    /// The run is not tied to the request's abort token. Leaving the page mid run would otherwise cancel it after
    /// some records were pushed, and those pushes would be forgotten.
    /// </remarks>
    /// <returns>The outcome per record.</returns>
    [HttpPost("Update")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DNSUpdateOutcome>> UpdateNow()
        => Ok(await _updateService.RunAsync(CancellationToken.None, force: true).ConfigureAwait(false));

    /// <summary>
    /// Clears a record's status, so it starts over with no pause and no last pushed address.
    /// </summary>
    /// <param name="id">The record's id.</param>
    /// <returns>204.</returns>
    [HttpPost("Records/{id}/Reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResetRecord([FromRoute] string id)
    {
        _statusStore.RemoveRecords(_statusStore.Load().Records.Keys.Where(k => string.Equals(RecordExpansion.SourceId(k), id, StringComparison.Ordinal)));
        return NoContent();
    }

    /// <summary>
    /// Whether the provider of this kind asks for a hostname. Providers that work the host out from the account
    /// hide the field, so their records are saved without one. An unknown kind needs one.
    /// </summary>
    /// <param name="providers">The registered providers.</param>
    /// <param name="kind">The record's provider.</param>
    /// <returns>True when the hostname field is shown for this provider.</returns>
    internal static bool NeedsHostname(IEnumerable<IDNSProvider> providers, DNSProviderKind kind)
    {
        var provider = providers.FirstOrDefault(p => p.Kind == kind);
        return provider is null || !string.IsNullOrEmpty(provider.Fields.Hostname);
    }

    /// <summary>
    /// Whether a record with this provider can follow the proxy's host names. That needs a hostname field that holds a
    /// name, so providers that work the host out from the account or take a record id cannot.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>True when the provider's hostname field holds a DNS name.</returns>
    internal static bool CanFollowProxy(IDNSProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return !string.IsNullOrEmpty(provider.Fields.Hostname) && !RecordTarget.HoldsRecordId(provider.Kind);
    }

    /// <summary>
    /// Turns a submitted credential into the value to save: the kept marker keeps the stored value, an empty field
    /// clears it, and anything typed is encrypted.
    /// </summary>
    /// <param name="submitted">The value sent.</param>
    /// <param name="stored">The stored value, or <c>null</c> when it may not be kept.</param>
    /// <returns>The value to save.</returns>
    internal string ResolveSecret(string? submitted, string? stored) => _secrets.ResolveIncoming(submitted, stored ?? string.Empty);

    /// <summary>
    /// Gives a following record as it is pushed for one host.
    /// </summary>
    /// <param name="record">The stored record.</param>
    /// <param name="host">One host it follows.</param>
    /// <returns>A copy named after the host.</returns>
    internal static DNSRecord Followed(DNSRecord record, string host)
    {
        var copy = record.Clone();
        copy.Hostname = host;
        copy.Id = record.Id + RecordExpansion.Separator + host;
        return copy;
    }

    private ActionResult<DNSRecord> Save(DNSRecord record, DNSRecord? existing)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        // Checked before anything is changed: XML serialization fails partway on an unknown provider and leaves a
        // truncated settings file behind. Status is the store's, so whatever the page sent for it is dropped.
        if (!Enum.IsDefined(record.Provider))
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorUnknownProvider)));
        }

        new RecordStatus().ApplyTo(record);
        record.Hostname = (record.Hostname ?? string.Empty).Trim();
        record.Name = (record.Name ?? string.Empty).Trim();
        DdnsLimits.Clamp(record);
        if (XmlText.FindInvalid(record) is { } bad)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ErrorInvalidCharacter, bad)));
        }

        if (record.Hostname.Length == 0 && !record.FollowProxyHosts && NeedsHostname(_providers, record.Provider))
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorRecordNeedsHostname)));
        }

        // A provider without a name field would push a route's name as a record id, or ignore it.
        if (record.FollowProxyHosts && _providers.FirstOrDefault(p => p.Kind == record.Provider) is { } chosen && !CanFollowProxy(chosen))
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorCannotFollowProxy)));
        }

        // Encrypted values are never sent by the page, which shows a marker instead, so one arriving here was
        // copied from somewhere else.
        if (SecretProtector.IsProtected(record.Login) || SecretProtector.IsProtected(record.Password))
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorEncryptedCredentials)));
        }

        // Without the key store a typed credential could only be stored in the clear, which is never done.
        if (!_secrets.IsAvailable && (IsTyped(record.Login) || IsTyped(record.Password)))
        {
            return BadRequest(Errors(CredentialEncryption.KeyStoreUnavailable));
        }

        // Kept credentials stay with where they are sent: a record moved to another provider or server starts
        // without them, so an edit can never send a saved password somewhere new.
        var prior = existing is not null && RecordMerge.SameDestination(existing, record) ? existing : null;
        record.Login = ResolveSecret(record.Login, prior?.Login);
        record.Password = ResolveSecret(record.Password, prior?.Password);

        List<DNSRecord>? before = null;
        List<DNSRecord>? after = null;
        plugin.MutateConfiguration(c =>
        {
            before = c.Ddns.Records;
            after = c.Ddns.Records.Select(r => string.Equals(r.Id, record.Id, StringComparison.Ordinal) ? record : r).ToList();
            if (existing is null)
            {
                after.Add(record);
            }

            c.Ddns.Records = after;
            return true;
        });

        ConfigurationAudit.LogRecordChanges(_activity, before, after, ActivityUser.Of(User), ActivityUser.NameOf(User));

        // A record pointed somewhere else would otherwise inherit the old target's backoff and last pushed addresses.
        if (existing is not null && (!RecordTarget.SameTarget(existing, record) || existing.FollowProxyHosts != record.FollowProxyHosts))
        {
            _statusStore.RemoveRecords(_statusStore.Load().Records.Keys.Where(k => string.Equals(RecordExpansion.SourceId(k), record.Id, StringComparison.Ordinal)));
        }

        return Ok(Redact(record, _statusStore.Load().Records.GetValueOrDefault(record.Id)));
    }

    // A value the person typed, as opposed to the kept marker or an empty field.
    private static bool IsTyped(string? submitted)
        => !string.IsNullOrEmpty(submitted)
            && !string.Equals(submitted, SecretProtector.KeptSentinel, StringComparison.Ordinal);

    private static DdnsSettings SettingsOnly(DdnsSettings settings) => new()
    {
        IPv4DetectionUrl = settings.IPv4DetectionUrl,
        IPv6DetectionUrl = settings.IPv6DetectionUrl,
        ForceUpdateHours = settings.ForceUpdateHours,
        SkipInternalAddresses = settings.SkipInternalAddresses,
        BackoffAfterFailures = settings.BackoffAfterFailures,
        BackoffHours = settings.BackoffHours,
        RequestTimeoutSeconds = settings.RequestTimeoutSeconds
    };

    /// <summary>
    /// Copies a record for the page, with each stored credential replaced by the kept marker and its status filled in.
    /// </summary>
    /// <param name="record">The stored record.</param>
    /// <param name="status">Its status, or <c>null</c> before its first run.</param>
    /// <returns>The copy.</returns>
    internal static DNSRecord Redact(DNSRecord record, RecordStatus? status)
    {
        var copy = record.WithSecrets(
            string.IsNullOrEmpty(record.Login) ? string.Empty : SecretProtector.KeptSentinel,
            string.IsNullOrEmpty(record.Password) ? string.Empty : SecretProtector.KeptSentinel);
        (status ?? new RecordStatus()).ApplyTo(copy);
        return copy;
    }

    private string Reply(string key, params object?[] args) => ServerText.Reply(Request, key, args);

    private static ApiError Errors(string error) => new(error);
}
