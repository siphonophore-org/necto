using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Moves users onto Necto's password rule provider while password rules hold for them, and back to the provider they
/// had before once none do. Administrators are never moved onto it, and a promoted member is moved back.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class PasswordEnrollment
{
    /// <summary>The provider Jellyfin gives every account it creates.</summary>
    public const string DefaultProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly AccountActivity _activity;
    private readonly ILogger<PasswordEnrollment> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordEnrollment"/> class.
    /// </summary>
    /// <param name="store">Reads the groups and keeps each user's provider from before.</param>
    /// <param name="userManager">Lists the users and changes their provider.</param>
    /// <param name="activity">Sums the moves into activity entries.</param>
    /// <param name="logger">Writes the server log.</param>
    public PasswordEnrollment(IConfigurationStore store, IUserManager userManager, AccountActivity activity, ILogger<PasswordEnrollment> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _userManager = userManager;
        _activity = activity;
        _logger = logger;
    }

    /// <summary>Gets the id of Necto's password rule provider.</summary>
    public static string ProviderId { get; } = typeof(PasswordRuleAuthenticationProvider).FullName!;

    /// <summary>
    /// Moves every user onto or off the password rule provider, by whether password rules hold for them.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass between users.</param>
    /// <returns>How many users moved.</returns>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var moved = 0;
        foreach (var user in _userManager.GetUsers().ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ReconcileUserAsync(user).ConfigureAwait(false))
            {
                moved++;
            }
        }

        return moved;
    }

    /// <summary>
    /// Moves one user onto or off the password rule provider, by whether password rules hold for them.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><c>true</c> when the user moved.</returns>
    public async Task<bool> ReconcileUserAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var isAdmin = user.HasPermission(PermissionKind.IsAdministrator);
        var ruled = !isAdmin && _store.Read(c => AccountRuleMerger.Password(GroupMembership.GroupsOf(c, user.Id))) is not null;
        try
        {
            return ruled ? await EnrollAsync(user).ConfigureAwait(false) : await UnenrollAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not move {UserId} onto or off the password rules", user.Id);
            return false;
        }
    }

    /// <summary>
    /// Moves a user onto the password rule provider, keeping the provider they had. Only a user on Jellyfin's own
    /// provider moves, since an account signed in elsewhere, such as LDAP, has no local password to check.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><c>true</c> when the user moved.</returns>
    public async Task<bool> EnrollAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.Equals(user.AuthenticationProviderId, ProviderId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(user.AuthenticationProviderId, DefaultProviderId, StringComparison.Ordinal))
        {
            _logger.LogDebug("Left {UserId} on {Provider}, which password rules cannot check", user.Id, user.AuthenticationProviderId);
            return false;
        }

        var original = user.AuthenticationProviderId;
        _store.Mutate(c =>
        {
            if (c.ProviderEnrollments.Any(e => e.UserId == user.Id))
            {
                return false;
            }

            c.ProviderEnrollments.Add(new ProviderEnrollment { UserId = user.Id, OriginalProviderId = original });
            return true;
        });

        await SetProviderAsync(user, ProviderId).ConfigureAwait(false);
        _activity.Add(AccountActivity.PasswordRulesEnrolled, user.Username);
        return true;
    }

    /// <summary>
    /// Moves a user off the password rule provider, back to the one they had, or Jellyfin's own when none was kept.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><c>true</c> when the user moved.</returns>
    public async Task<bool> UnenrollAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!string.Equals(user.AuthenticationProviderId, ProviderId, StringComparison.Ordinal))
        {
            return false;
        }

        var restore = DefaultProviderId;
        _store.Mutate(c =>
        {
            var record = c.ProviderEnrollments.FirstOrDefault(e => e.UserId == user.Id);
            if (record is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(record.OriginalProviderId))
            {
                restore = record.OriginalProviderId;
            }

            c.ProviderEnrollments.RemoveAll(e => e.UserId == user.Id);
            return true;
        });

        await SetProviderAsync(user, restore).ConfigureAwait(false);
        _activity.Add(AccountActivity.PasswordRulesRemoved, user.Username);
        return true;
    }

    /// <summary>
    /// Changes a user's provider through the policy, as Jellyfin's own provider list does.
    /// </summary>
    private async Task SetProviderAsync(User user, string providerId)
    {
        // The policy is built from the account as stored, since the entity the caller holds may predate a write.
        var current = _userManager.GetUserById(user.Id) ?? user;
        var policy = _userManager.GetUserDto(current, string.Empty).Policy;
        policy.AuthenticationProviderId = providerId;
        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
    }
}
