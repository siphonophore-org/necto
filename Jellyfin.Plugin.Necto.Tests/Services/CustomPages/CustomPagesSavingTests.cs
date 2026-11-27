using System;
using System.IO;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.CustomPages;

/// <summary>
/// A save gives every page an id, brings the new store values into range, refuses invalid settings, and encrypts route
/// passwords under the Necto key ring.
/// </summary>
public sealed class CustomPagesSavingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "necto-pages-keys-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static SecretProtector Plain() => new("Necto.Tests", NullLogger.Instance);

    /// <summary>
    /// A page saved without an id gets one, and a page with one keeps it.
    /// True: a group can name a page that survives a change of slug.
    /// False: a new page cannot be given to a group, or a renamed page loses its groups.
    /// </summary>
    [Fact]
    public void Apply_GivesAnIdOnlyToAPageWithout()
    {
        var incoming = new PluginConfiguration();
        incoming.CustomPages.Pages.Add(new CustomPage { Slug = "new" });
        incoming.CustomPages.Pages.Add(new CustomPage { Id = "kept", Slug = "old" });

        CustomPagesSaving.Apply(incoming, new PluginConfiguration(), Plain());

        Assert.False(string.IsNullOrEmpty(incoming.CustomPages.Pages[0].Id));
        Assert.Equal("kept", incoming.CustomPages.Pages[1].Id);
    }

    /// <summary>
    /// The values a new store starts with come into the allowed range.
    /// True: a new store never starts with a limit the validator refuses.
    /// False: a bad Settings value breaks Add store.
    /// </summary>
    [Fact]
    public void Apply_BringsTheNewStoreValuesIntoRange()
    {
        var incoming = new PluginConfiguration();
        incoming.CustomPages.NewStoreMaxRecords = 0;
        incoming.CustomPages.NewStoreRetentionDays = 99999;

        CustomPagesSaving.Apply(incoming, new PluginConfiguration(), Plain());

        Assert.Equal(1, incoming.CustomPages.NewStoreMaxRecords);
        Assert.Equal(PageStore.RetentionDayCeiling, incoming.CustomPages.NewStoreRetentionDays);
    }

    /// <summary>
    /// Settings the validator refuses are not saved.
    /// True: a save from any API client is held to the same rules as the page.
    /// False: two pages could share a slug.
    /// </summary>
    [Fact]
    public void Apply_RefusesInvalidSettings()
    {
        var incoming = new PluginConfiguration();
        incoming.CustomPages.Pages.Add(new CustomPage { Slug = "dup" });
        incoming.CustomPages.Pages.Add(new CustomPage { Slug = "dup" });

        Assert.Throws<ArgumentException>(() => CustomPagesSaving.Apply(incoming, new PluginConfiguration(), Plain()));
    }

    /// <summary>
    /// A save with no Pages settings at all gets empty ones.
    /// True: a save from another tool's section never wipes or breaks Pages.
    /// False: a configuration without the section fails to save.
    /// </summary>
    [Fact]
    public void Apply_FillsMissingSettings()
    {
        var incoming = new PluginConfiguration { CustomPages = null! };

        CustomPagesSaving.Apply(incoming, new PluginConfiguration(), Plain());

        Assert.NotNull(incoming.CustomPages);
        Assert.Empty(incoming.CustomPages.Pages);
    }

    /// <summary>
    /// A new route password is stored encrypted, and the services' protector reads it back.
    /// True: the save and the route forwarder share one key ring under the Necto name.
    /// False: a saved password cannot be read when the route is called.
    /// </summary>
    [Fact]
    public void Apply_EncryptsUnderTheKeyRingTheServicesRead()
    {
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_PluginConfigurationsPath", _ => _root);
        var incoming = new PluginConfiguration();
        incoming.CustomPages.Pages.Add(new CustomPage
        {
            Slug = "p",
            ApiRoutes = { new PageApiRoute { Name = "rpc", Url = "http://nas.example.com/rpc", Password = "hunter2" } }
        });

        CustomPagesSaving.Apply(incoming, new PluginConfiguration(), paths);
        var stored = incoming.CustomPages.Pages[0].ApiRoutes[0].Password;
        var reader = StableSecretProtection.CreateProtector(paths, CustomPagesSaving.SecretsName, NullLogger.Instance);

        Assert.NotEqual("hunter2", stored);
        Assert.Equal("hunter2", reader.Unprotect(stored));
        Assert.Equal(Path.Combine(_root, "Jellyfin.Plugin.Necto.Keys"), StableSecretProtection.KeyDirectory(paths, CustomPagesSaving.SecretsName));
    }
}
