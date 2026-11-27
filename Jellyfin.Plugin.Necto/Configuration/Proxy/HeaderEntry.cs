namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// A header a route sets on what it forwards or on what it answers.
/// </summary>
public class HeaderEntry
{
    /// <summary>Gets or sets the header's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the header's value.</summary>
    public string Value { get; set; } = string.Empty;
}
