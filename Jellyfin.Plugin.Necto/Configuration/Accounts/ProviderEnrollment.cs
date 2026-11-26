using System;

namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The authentication provider a user had before password rules moved them onto Necto's, so it can be put back.
/// </summary>
public class ProviderEnrollment
{
    /// <summary>Gets or sets the user's id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the provider the user had before.</summary>
    public string OriginalProviderId { get; set; } = string.Empty;
}
