using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.Notifications;

/// <summary>
/// The SMTP server email channels send through.
/// </summary>
public class SmtpSettings
{
    /// <summary>Gets or sets the server's host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the server's port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Gets or sets a value indicating whether the connection is encrypted with STARTTLS.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Gets or sets the user name to sign in with, or empty to send without one.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the password. It lives in the Notifications secrets file and reaches a page only as kept.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the address email comes from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the name email comes from. Blank is Jellyfin.</summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a server and a sender are set, so email can be sent.</summary>
    public bool IsSet => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}
