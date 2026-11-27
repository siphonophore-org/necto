using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.MenuLinks;

/// <summary>
/// Reads and writes the menuLinks of the web client's config.json, the custom links in its side menu. Every other
/// key of the file stays as it was, in its place. One change runs at a time, from the editor and from the links
/// Necto adds itself.
/// </summary>
public static class MenuLinksFile
{
    /// <summary>The most links the editor keeps.</summary>
    public const int MaxLinks = 50;

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    /// <summary>Gets the lock every change to the file takes, so two changes never write over each other.</summary>
    internal static SemaphoreSlim Lock { get; } = new(1, 1);

    /// <summary>Gives where the web client's config.json is, beside the web client, or null when Jellyfin serves no web client.</summary>
    /// <param name="paths">The server's paths.</param>
    /// <returns>The path, or null.</returns>
    public static string? PathOf(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return string.IsNullOrEmpty(paths.WebPath) ? null : Path.Combine(paths.WebPath, "config.json");
    }

    /// <summary>Reads the links from a web client config, skipping any entry that is not an object.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>The links, in their order.</returns>
    public static List<MenuLink> LinksOf(JsonObject web)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            return new List<MenuLink>();
        }

        return links.OfType<JsonObject>().Select(l => new MenuLink
        {
            Name = TextOf(l, "name"),
            Icon = TextOf(l, "icon"),
            Url = TextOf(l, "url")
        }).ToList();
    }

    /// <summary>
    /// Puts links into a web client config as its menuLinks, in their order. The menuLinks key keeps its place, or is
    /// added at the end, and every other key stays. A link's own keys beyond name, icon, and url are kept when the
    /// link at its place had them and still goes to the same address.
    /// </summary>
    /// <param name="web">The web client's config.json.</param>
    /// <param name="links">The links.</param>
    public static void SetLinks(JsonObject web, IReadOnlyList<MenuLink> links)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(links);
        var before = web["menuLinks"] is JsonArray old ? old.OfType<JsonObject>().ToList() : new List<JsonObject>();
        var array = new JsonArray();
        foreach (var link in links)
        {
            var node = new JsonObject { ["name"] = link.Name.Trim() };
            if (!string.IsNullOrWhiteSpace(link.Icon))
            {
                node["icon"] = link.Icon.Trim();
            }

            node["url"] = link.Url.Trim();

            // Keys the web client may read that the editor does not show, from the old entry to the same address.
            var same = before.FirstOrDefault(b => string.Equals(TextOf(b, "url"), node["url"]!.GetValue<string>(), StringComparison.Ordinal));
            if (same is not null)
            {
                before.Remove(same);
                foreach (var (key, value) in same.ToList())
                {
                    if (key is not ("name" or "icon" or "url"))
                    {
                        node[key] = value?.DeepClone();
                    }
                }
            }

            array.Add(node);
        }

        web["menuLinks"] = array;
    }

    /// <summary>Gives a problem with a link the editor sent, or null when it is fine.</summary>
    /// <param name="link">The link.</param>
    /// <returns>The translation key of the problem, or null.</returns>
    public static string? ProblemOf(MenuLink? link)
    {
        if (link is null || string.IsNullOrWhiteSpace(link.Name))
        {
            return TranslationKeys.MenuLinksErrorNameRequired;
        }

        if (string.IsNullOrWhiteSpace(link.Url))
        {
            return TranslationKeys.MenuLinksErrorUrlRequired;
        }

        var icon = (link.Icon ?? string.Empty).Trim();
        if (icon.Length > 0 && (icon.Length > 64 || !icon.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')))
        {
            return TranslationKeys.MenuLinksErrorIcon;
        }

        return link.Name.Length > 100 || link.Url.Length > 2048 ? TranslationKeys.MenuLinksErrorTooLong : null;
    }

    /// <summary>Says whether a file can be written in place, without changing it.</summary>
    /// <param name="path">The file.</param>
    /// <returns><c>true</c> when it opens for writing.</returns>
    public static bool CanWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Reads the web client config as an object.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The config, or null when the file is not a JSON object.</returns>
    public static async Task<JsonObject?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
    }

    /// <summary>Writes the web client config back, indented.</summary>
    /// <param name="path">The file.</param>
    /// <param name="web">The config.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public static Task WriteAsync(string path, JsonObject web, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(web);
        return WriteTextAsync(path, web.ToJsonString(_indented), cancellationToken);
    }

    /// <summary>
    /// Writes text beside the file and swaps it in, so the web client never reads half a file. Where the folder cannot
    /// be written, as in Docker images where config.json is mounted on its own, the file itself is written instead.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="json">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task WriteTextAsync(string path, string json, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await AtomicFile.WriteAsync(path, (file, ct) => file.WriteAsync(bytes, ct).AsTask(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A file mounted on its own can be written but not replaced.
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string TextOf(JsonObject node, string key)
        => node[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : string.Empty;
}
