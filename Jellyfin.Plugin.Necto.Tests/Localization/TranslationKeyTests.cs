using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Localization;

/// <summary>
/// Every key a page in Web/ names has a string, in Localization/en-us.json or as a declared Jellyfin string.
/// True: each data-str attribute and each tr, format, plural, and tabName call finds its text.
/// False: a page names a key nothing defines, so it would show the key in place of the words.
/// </summary>
public class TranslationKeyTests
{
    // Markup attributes, and the first literal key of a tr, format, or tabName call.
    private static readonly Regex KeyUse = new(
        @"data-str(?:-title|-label|-placeholder)?=""([A-Za-z0-9_.]+)""|\b(?:tr|format|tabName)\(\s*'([A-Za-z0-9_.]+)'",
        RegexOptions.Compiled);

    // The two keys of a plural call, which name the one and the many forms after the count.
    private static readonly Regex PluralCall = new(@"\bplural\([^']*'([A-Za-z0-9_.]+)'\s*,\s*'([A-Za-z0-9_.]+)'", RegexOptions.Compiled);

    private static readonly Regex Declared = new(@"<JellyfinString Include=""([^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryKeyUsedInWeb_HasAString()
    {
        var project = ProjectFolder();
        var known = new HashSet<string>(StringComparer.Ordinal);
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "Localization", "en-us.json"))))
        {
            known.UnionWith(json.RootElement.EnumerateObject().Select(p => p.Name));
        }

        known.UnionWith(Declared.Matches(File.ReadAllText(Path.Combine(project, "Jellyfin.Plugin.Necto.csproj"))).Select(m => m.Groups[1].Value));

        var missing = new List<string>();
        var used = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(project, "Web")).Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var keys = KeyUse.Matches(text).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Concat(PluralCall.Matches(text).SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }));
            foreach (var key in keys)
            {
                used++;
                if (!known.Contains(key))
                {
                    missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
        }

        Assert.True(used > 0, "No page names a key, so the patterns miss the pages.");
        Assert.True(missing.Count == 0, "Keys with no string:\n" + string.Join('\n', missing.Distinct()));
    }

    /// <summary>
    /// Every key the request page and its web client script name is a string, as their own str and tr calls name them.
    /// True: the request page shows words in place of every key, in each language Jellyfin has.
    /// False: a key the page names would show as the key itself.
    /// </summary>
    [Fact]
    public void RequestPage_EveryKeyItUses_IsBuiltIn()
    {
        var english = Siphonophore.Stolon.StolonTranslations.For("en-us");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "necto_requests_page.html", "necto_requests_webclient.js" })
        {
            var page = Jellyfin.Plugin.Necto.Services.Requests.EmbeddedResources.Read(name);
            foreach (Match m in Regex.Matches(page, @"\b(?:str|tr)\('([A-Za-z0-9_.]+)'|data-(?:str|tr(?:-label|-title)?)=""([A-Za-z0-9_.]+)""|title: '([A-Za-z]+)', statuses|label: '([A-Za-z]+)'"))
            {
                used.Add(m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
            }

            // Keys picked between two in one call: str(cond ? 'A' : 'B').
            foreach (Match m in Regex.Matches(page, @"\b(?:str|tr)\([^()]*?\?\s*'([A-Za-z0-9_]+)'\s*:\s*'([A-Za-z0-9_]+)'"))
            {
                used.Add(m.Groups[1].Value);
                used.Add(m.Groups[2].Value);
            }
        }

        Assert.True(used.Count > 100, "The patterns found only " + used.Count + " keys, so they miss the page.");
        Assert.All(used, k => Assert.True(english.ContainsKey(k), k + " is neither a local string nor a declared Jellyfin string"));
    }

    /// <summary>
    /// US English is built in with the declared Jellyfin strings, which come in Jellyfin's own languages, and a plugin
    /// string falls back to US English.
    /// True: the request page reads Favoriten in German and its own words in English where none are translated.
    /// False: a Jellyfin string would be missing, or a language without the plugin's string would show its key.
    /// </summary>
    [Fact]
    public void For_Languages_HoldTheDeclaredJellyfinStrings()
    {
        var english = Siphonophore.Stolon.StolonTranslations.For("en-us");

        Assert.Contains(Siphonophore.Stolon.StolonTranslations.FallbackLanguage, Siphonophore.Stolon.StolonTranslations.Languages);
        Assert.Equal("Favorites", english[Siphonophore.Stolon.TranslationKeys.Favorites]);
        Assert.Equal("Favoriten", Siphonophore.Stolon.StolonTranslations.Get(Siphonophore.Stolon.TranslationKeys.Favorites, "de"));
        Assert.Equal(english[Siphonophore.Stolon.TranslationKeys.TabRequests], Siphonophore.Stolon.StolonTranslations.Get(Siphonophore.Stolon.TranslationKeys.TabRequests, null));
        Assert.Equal(english[Siphonophore.Stolon.TranslationKeys.RequestsErrorNotOutYet], Siphonophore.Stolon.StolonTranslations.Get(Siphonophore.Stolon.TranslationKeys.RequestsErrorNotOutYet, "de"));
    }

    /// <summary>
    /// Finds the plugin's project folder from this file's own path.
    /// </summary>
    private static string ProjectFolder([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Jellyfin.Plugin.Necto"));
}
