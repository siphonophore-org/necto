using System;
using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.Invites;

/// <summary>
/// A signup link. Anyone with the link, and its PIN when it has one, may create one account until the invite expires,
/// is used up, or is locked.
/// </summary>
public class Invite
{
    /// <summary>Gets or sets the invite's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the random token in the link, which names the invite.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets or sets the name shown in the Invites tool. The signup page never shows it.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the welcome message the signup page shows.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the links shown to a new user once the account is made.</summary>
    public List<InviteResource> Resources { get; set; } = new();

    /// <summary>Gets or sets the salted hash of the PIN, in Jellyfin's password hash format, or empty for none.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string PinHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the group a new account joins, Everyone's id for no group, or <c>null</c> for the group set for new users when the link is used.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Gets or sets the day the invite stops working, or <c>null</c> for never.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Gets or sets how many accounts the invite may make. 0 is no limit.</summary>
    public int MaxUses { get; set; }

    /// <summary>Gets or sets a value indicating whether the invite works.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets when the invite was made, in UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the user who made the invite, or <see cref="Guid.Empty"/> for one made in the Invites tool.</summary>
    public Guid CreatedBy { get; set; }
}
