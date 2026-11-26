using System;
using System.IO;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// Proves the shared protector reads the secrets the plugin stored before it moved onto the base package:
/// the key folder, Data Protection application name, and purpose are unchanged, so a secret encrypted under
/// the old hand-built values still decrypts.
/// </summary>
public class StableSecretProtectionTests
{
    private static string TempDir() =>
        Path.Join(Path.GetTempPath(), "remoteaccess-keys-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecretsName_DerivesThePreviousKeyFolderAppNameAndPurpose()
    {
        var dir = TempDir();
        try
        {
            var paths = Stubs.Paths(dir);

            Assert.Equal("Jellyfin.Plugin.Necto", DdnsServices.SecretsName);
            Assert.Equal(
                Path.Join(dir, "Jellyfin.Plugin.Necto.Keys"),
                StableSecretProtection.KeyDirectory(paths, DdnsServices.SecretsName));

            // Encrypt the way Data Protection does by hand: keys in <configs>/Jellyfin.Plugin.Necto.Keys, the
            // application name Jellyfin.Plugin.Necto, and the purpose Jellyfin.Plugin.Necto.Secrets.v1.
            var services = new ServiceCollection();
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Join(dir, "Jellyfin.Plugin.Necto.Keys")))
                .SetApplicationName("Jellyfin.Plugin.Necto");
            string stored;
            using (var container = services.BuildServiceProvider())
            {
                var legacy = new SecretProtector(
                    "Jellyfin.Plugin.Necto.Secrets.v1",
                    NullLogger.Instance,
                    container.GetRequiredService<IDataProtectionProvider>());
                stored = legacy.Protect("super-secret-password");
            }

            Assert.True(SecretProtector.IsProtected(stored));

            // A fresh protector built by the shared helper from the same configurations folder reads it.
            var current = StableSecretProtection.CreateProtector(paths, DdnsServices.SecretsName, NullLogger.Instance);
            Assert.True(current.IsAvailable);
            Assert.Equal("super-secret-password", current.Unprotect(stored));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void Build_RoundTripsWithKeysInTheGivenDirectory()
    {
        var dir = TempDir();
        try
        {
            var provider = StableSecretProtection.Build(dir, DdnsServices.SecretsName, NullLogger.Instance);
            Assert.NotNull(provider);

            var protector = provider!.CreateProtector("Jellyfin.Plugin.Necto.Secrets.v1");
            var encrypted = protector.Protect("api-token");

            Assert.NotEqual("api-token", encrypted);
            Assert.Equal("api-token", protector.Unprotect(encrypted));
            Assert.NotEmpty(Directory.GetFiles(dir, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void CreateProtector_DegradesToPlaintext_WhenKeyDirectoryUnusable()
    {
        // A file where the configurations folder should be makes the key folder impossible to create.
        var file = Path.GetTempFileName();
        try
        {
            var paths = Stubs.Paths(file);

            var protector = StableSecretProtection.CreateProtector(paths, DdnsServices.SecretsName, NullLogger.Instance);

            Assert.False(protector.IsAvailable);
            Assert.Equal("hunter2", protector.Protect("hunter2"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
