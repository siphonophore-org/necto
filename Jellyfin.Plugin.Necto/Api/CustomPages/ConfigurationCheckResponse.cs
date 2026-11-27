namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// The answer of <c>POST /Pages/Configuration/Check</c>: whether a configuration would be saved, and why not.
/// </summary>
public class ConfigurationCheckResponse
{
    /// <summary>Gets or sets a value indicating whether saving the configuration would succeed.</summary>
    public bool Ok { get; set; }

    /// <summary>Gets or sets why the save would be refused, or <c>null</c> when it would succeed.</summary>
    public string? Message { get; set; }
}
