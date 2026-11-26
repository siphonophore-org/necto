using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;

/// <summary>
/// A page service that finds the pages and images a test gives it and answers with fixed HTML, counting renders, so a
/// controller test sees whether a page body was ever composed.
/// </summary>
public sealed class StubPageService : IPageService
{
    public Dictionary<string, CustomPage> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PageAsset> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<PageAsset, byte[]> Bytes { get; } = new();

    public string RenderHtml { get; set; } = "<html></html>";

    public string ShellHtml { get; set; } = "<html>shell</html>";

    public int Renders { get; private set; }

    public int AssetReads { get; private set; }

    public List<(string Slug, PageVisibility Visibility, string? Language)> Shells { get; } = new();

    public CustomPage? Find(string slug) => Pages.GetValueOrDefault(slug);

    public string Render(CustomPage page)
    {
        Renders++;
        return RenderHtml;
    }

    public string GetShellHtml(string slug, PageVisibility visibility, string? language)
    {
        Shells.Add((slug, visibility, language));
        return ShellHtml;
    }

    public string NotFoundHtml(string slug, string? language) => "<html>missing</html>";

    public PageAsset? FindAsset(string name) => Assets.GetValueOrDefault(name);

    public byte[]? GetAssetBytes(PageAsset asset)
    {
        AssetReads++;
        return Bytes.GetValueOrDefault(asset);
    }
}
