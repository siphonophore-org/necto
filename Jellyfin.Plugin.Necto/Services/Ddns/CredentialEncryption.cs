using System;
using System.Collections.Generic;
using System.Linq;
using Siphonophore.Stolon;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// Encrypts record credentials in place. Shared by every path that can persist the configuration, so a
/// plaintext secret is caught at the door no matter which endpoint delivered it.
/// </summary>
public static class CredentialEncryption
{
    /// <summary>
    /// What the person is told when credentials cannot be encrypted. They are never stored in the clear instead.
    /// </summary>
    public static string KeyStoreUnavailable => ServerText.Get(TranslationKeys.DdnsErrorKeyStoreUnavailable);

    /// <summary>Whether a stored value is a credential in the clear: not empty and not encrypted.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>True for a plaintext credential.</returns>
    public static bool IsPlaintext(string? value) => !string.IsNullOrEmpty(value) && !SecretProtector.IsProtected(value);

    /// <summary>
    /// Clears every credential held in the clear. Used while the key store is unavailable, so none stays on disk
    /// unencrypted. Encrypted values are kept for when the keys are back.
    /// </summary>
    /// <param name="records">The records, changed in place.</param>
    /// <returns>Whether any value was cleared.</returns>
    public static bool DropPlaintext(IEnumerable<DNSRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var changed = false;
        foreach (var record in records)
        {
            if (IsPlaintext(record.Login))
            {
                record.Login = string.Empty;
                changed = true;
            }

            if (IsPlaintext(record.Password))
            {
                record.Password = string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Clears encrypted credentials a client sent that are not the stored value of that same record, still
    /// sent to the same provider and server. Encrypted text copied from another field, or kept on a record
    /// moved elsewhere, would otherwise be decrypted and sent wherever the record now points.
    /// </summary>
    /// <param name="incoming">The records about to be saved, changed in place.</param>
    /// <param name="stored">The records currently saved.</param>
    public static void DropForeignCiphertext(IEnumerable<DNSRecord> incoming, IEnumerable<DNSRecord>? stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var previous = new Dictionary<string, DNSRecord>(StringComparer.Ordinal);
        foreach (var record in (stored ?? Array.Empty<DNSRecord>()).Where(r => !string.IsNullOrEmpty(r.Id)))
        {
            previous.TryAdd(record.Id, record);
        }

        foreach (var record in incoming)
        {
            var prior = !string.IsNullOrEmpty(record.Id) && previous.TryGetValue(record.Id, out var match) && RecordMerge.SameDestination(match, record)
                ? match
                : null;
            record.Login = OwnOrEmpty(record.Login, prior?.Login);
            record.Password = OwnOrEmpty(record.Password, prior?.Password);
        }
    }

    /// <summary>
    /// Keeps an encrypted value only when it is exactly the stored one; plain values pass through.
    /// </summary>
    /// <param name="submitted">The value sent.</param>
    /// <param name="stored">The value stored for the same field, or null when it may not be kept.</param>
    /// <returns>The value to save.</returns>
    public static string OwnOrEmpty(string? submitted, string? stored)
        => SecretProtector.IsProtected(submitted) && !string.Equals(submitted, stored, StringComparison.Ordinal)
            ? string.Empty
            : submitted ?? string.Empty;

    /// <summary>
    /// Protects the login and password of every record. Values that are already encrypted, or empty,
    /// pass through unchanged.
    /// </summary>
    /// <param name="records">The records to protect.</param>
    /// <param name="secrets">The secret protector.</param>
    /// <returns>Whether any value changed.</returns>
    public static bool ProtectAll(IEnumerable<DNSRecord> records, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(secrets);

        var changed = false;
        foreach (var record in records)
        {
            var login = secrets.Protect(record.Login);
            if (!string.Equals(login, record.Login, StringComparison.Ordinal))
            {
                record.Login = login;
                changed = true;
            }

            var password = secrets.Protect(record.Password);
            if (!string.Equals(password, record.Password, StringComparison.Ordinal))
            {
                record.Password = password;
                changed = true;
            }
        }

        return changed;
    }
}
