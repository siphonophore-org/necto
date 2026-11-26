namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// Which clients a <see cref="SessionCleanupRule"/> covers.
/// </summary>
public enum SessionClientMode
{
    /// <summary>Every client.</summary>
    All,

    /// <summary>Only the rule's listed clients.</summary>
    Only,

    /// <summary>Every client but the rule's listed clients.</summary>
    AllExcept
}
