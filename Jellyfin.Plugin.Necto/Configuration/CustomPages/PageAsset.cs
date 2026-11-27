namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// An image hosted for the pages, referenced from a page as <c>asset/{name}</c>.
/// </summary>
public class PageAsset
{
    /// <summary>Gets or sets the file name used in its address, such as <c>logo.png</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the content type, such as <c>image/png</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the bytes, in Base64.</summary>
    public string DataBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets who may see the image. An Anyone image is public at <c>/pages/asset/{name}</c>. Any other is never
    /// served by address, since an image fetch carries no token, and is embedded as a <c>data:</c> address into the
    /// pages at or above its tier instead.
    /// </summary>
    public PageVisibility Visibility { get; set; } = PageVisibility.Anonymous;
}
