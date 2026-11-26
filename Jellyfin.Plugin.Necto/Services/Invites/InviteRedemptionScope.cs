using System;
using System.Threading;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// Marks the current async flow as an invite redemption. The new user event leaves the account to the redemption and
/// holds it disabled until its password is set, and the password rule provider takes the account's first password
/// as a creation, not a member's own change.
/// </summary>
public static class InviteRedemptionScope
{
    private static readonly AsyncLocal<bool> Active = new();

    /// <summary>Gets a value indicating whether an invite redemption is running on this async flow.</summary>
    public static bool IsActive => Active.Value;

    /// <summary>
    /// Begins a redemption scope. Dispose it to end the scope.
    /// </summary>
    /// <returns>A handle that ends the scope when disposed.</returns>
    public static IDisposable Begin()
    {
        Active.Value = true;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Active.Value = false;
    }
}
