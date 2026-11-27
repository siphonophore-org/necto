using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Finds pages and images and renders the HTML served for them, so the controller, where access is decided, can be
/// tested without the plugin.
/// </summary>
public interface IPageService
{
    /// <summary>
    /// Finds a published page by slug, ignoring case.
    /// </summary>
    /// <param name="slug">The slug.</param>
    /// <returns>The page, or <c>null</c> when no published page has the slug.</returns>
    CustomPage? Find(string slug);

    /// <summary>
    /// Renders a page in the frame that keeps the author's content apart from the viewer's session.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="contentStyle">A style block that goes first in the page's head, so the author's own styles win.</param>
    /// <returns>The whole document to serve.</returns>
    string Render(CustomPage page, string contentStyle = "");

    /// <summary>
    /// Gives the values of the loading screen of a page that needs a signed in viewer: its slug, its tier, and the
    /// sentences it shows. The screen's script asks for the page with the viewer's sign-in and writes it in.
    /// </summary>
    /// <param name="slug">The slug.</param>
    /// <param name="visibility">The page's tier.</param>
    /// <param name="language">The visitor's language, such as de, or <c>null</c> for US English.</param>
    /// <returns>The values, as text the screen encodes.</returns>
    IReadOnlyDictionary<string, string> LoaderValues(string slug, PageVisibility visibility, string? language);

    /// <summary>
    /// Finds an image by name, ignoring case.
    /// </summary>
    /// <param name="name">The image's name.</param>
    /// <returns>The image, or <c>null</c> when none has the name.</returns>
    PageAsset? FindAsset(string name);

    /// <summary>
    /// Decodes an image's bytes.
    /// </summary>
    /// <param name="asset">The image.</param>
    /// <returns>The bytes, or <c>null</c> when its Base64 is missing or invalid.</returns>
    byte[]? GetAssetBytes(PageAsset asset);
}
