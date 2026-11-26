namespace Jellyfin.Plugin.Necto.Configuration.Invites;

/// <summary>
/// A link shown to a new user once their account is made, such as a request page or a guide.
/// </summary>
public class InviteResource
{
    /// <summary>Gets or sets the button's text.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the absolute http or https address the button opens.</summary>
    public string Url { get; set; } = string.Empty;
}
