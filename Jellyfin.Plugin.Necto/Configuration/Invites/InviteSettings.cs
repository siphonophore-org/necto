namespace Jellyfin.Plugin.Necto.Configuration.Invites;

/// <summary>
/// The Invites settings, in the Invites section of Settings.
/// </summary>
public class InviteSettings
{
    /// <summary>Gets or sets a value indicating whether Invites is on. While it is off, no invite can be used.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the public address invite links start with. Blank uses the address the page is open at.</summary>
    public string LinkUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets how many accounts one invite may make in the window before it pauses. 0 is no limit.</summary>
    public int RateLimitCount { get; set; } = 10;

    /// <summary>Gets or sets the window the rate limit counts over, in minutes. 0 is no limit.</summary>
    public int RateLimitWindowMinutes { get; set; } = 60;

    /// <summary>Gets or sets how many wrong PINs lock an invite.</summary>
    public int MaxPinAttempts { get; set; } = 5;
}
