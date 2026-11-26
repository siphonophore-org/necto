using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds a secret protector with keys that live only for the test.
/// </summary>
public static class TestSecrets
{
    public static SecretProtector Create() => new("necto.test", NullLogger.Instance, new EphemeralDataProtectionProvider());
}
