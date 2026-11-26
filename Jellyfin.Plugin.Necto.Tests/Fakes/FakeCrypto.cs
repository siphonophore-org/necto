using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Model.Cryptography;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A stand-in for Jellyfin's crypto provider that stores a PIN as its plain bytes in Jellyfin's hash format, so a test
/// checks a PIN the way the server does without real hashing.
/// </summary>
public sealed class FakeCrypto : ICryptoProvider
{
    public string DefaultHashMethod => "FAKE";

    public PasswordHash CreatePasswordHash(ReadOnlySpan<char> password)
        => new("FAKE", Encoding.UTF8.GetBytes(password.ToString()), new byte[] { 1 }, new Dictionary<string, string>());

    public bool Verify(PasswordHash hash, ReadOnlySpan<char> password)
        => hash is not null && hash.Hash.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(password.ToString()));

    public byte[] GenerateSalt() => new byte[] { 1 };

    public byte[] GenerateSalt(int length) => new byte[length];
}
