using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Applies expiry and inactivity to every user who is not an administrator. A user whose groups have all expired is
/// disabled or deleted, and a user inactive past the window every group sets is disabled. A user who never signed in
/// is never disabled for inactivity.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class AccountRuleService
{
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly ActivityLogger _activity;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly TimeProvider _time;
    private readonly ILogger<AccountRuleService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountRuleService"/> class.
    /// </summary>
    /// <param name="store">Reads the groups, and drops deleted users from them.</param>
    /// <param name="userManager">Lists, disables, and deletes users.</param>
    /// <param name="activity">Writes one entry for each outcome of a run.</param>
    /// <param name="serverConfig">Gives the server's language for the entries.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="time">Gives today's date. Left out, it is the system clock.</param>
    public AccountRuleService(IConfigurationStore store, IUserManager userManager, ActivityLogger activity, IServerConfigurationManager serverConfig, ILogger<AccountRuleService> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _userManager = userManager;
        _activity = activity;
        _serverConfig = serverConfig;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Disables or deletes every expired user, and disables every inactive user.
    /// </summary>
    /// <param name="progress">Hears how far the run is, from 0 to 100.</param>
    /// <param name="cancellationToken">Stops the run between users.</param>
    /// <returns>How many users were disabled and deleted.</returns>
    public async Task<AccountRuleRun> ProcessAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var language = StolonTranslations.ServerLanguage(_serverConfig);
        var everyone = StolonTranslations.Get(TranslationKeys.GroupEveryone, language);
        var users = _userManager.GetUsers().Where(u => !u.HasPermission(PermissionKind.IsAdministrator)).ToList();
        var config = _store.Read(c => Siphonophore.Stolon.SecretJson.Copy(c));

        var deleted = new UserRunTally();
        var expired = new UserRunTally();
        var inactive = new UserRunTally();
        var deletedIds = new List<Guid>();

        // The entries are written even when the run is cancelled, so no change goes unrecorded.
        try
        {
            for (var i = 0; i < users.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var user = users[i];
                var group = GroupMembership.GroupOf(config, user.Id);
                var names = group.IsEveryone ? everyone : group.Name;
                try
                {
                    var dto = _userManager.GetUserDto(user, string.Empty);
                    var action = AccountRuleMerger.Expiry(group, now.Date);
                    if (action == ExpiryAction.Delete)
                    {
                        var username = user.Username;
                        await _userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
                        deletedIds.Add(user.Id);
                        deleted.Add(names, username);
                        _logger.LogDebug("Deleted expired user {UserId}", user.Id);
                    }
                    else if (action == ExpiryAction.Disable)
                    {
                        if (!dto.Policy.IsDisabled)
                        {
                            dto.Policy.IsDisabled = true;
                            await _userManager.UpdatePolicyAsync(user.Id, dto.Policy).ConfigureAwait(false);
                            expired.Add(names, user.Username, user.Id);
                            _logger.LogDebug("Disabled expired user {UserId}", user.Id);
                        }
                    }
                    else if (AccountRuleMerger.InactiveDays(group) is { } days
                        && (dto.LastActivityDate ?? dto.LastLoginDate) is { } last
                        && last < now.AddDays(-days)
                        && !dto.Policy.IsDisabled)
                    {
                        dto.Policy.IsDisabled = true;
                        await _userManager.UpdatePolicyAsync(user.Id, dto.Policy).ConfigureAwait(false);
                        inactive.Add(StolonTranslations.Format(days == 1 ? TranslationKeys.ActivityInactiveSourceOne : TranslationKeys.ActivityInactiveSourceMany, language, names, days), user.Username, user.Id);
                        _logger.LogDebug("Disabled inactive user {UserId}", user.Id);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not apply the account rules to {UserId}", user.Id);
                }

                progress?.Report((i + 1) * 100.0 / users.Count);
            }
        }
        finally
        {
            // A deleted account no longer exists, so its id is not attached to the entry.
            deleted.Log(_activity, UserRunWording.ExpiryDeleted, "Necto.ExpiryDeleted", language, attachUser: false);
            expired.Log(_activity, UserRunWording.ExpiryDisabled, "Necto.ExpiryDisabled", language);
            inactive.Log(_activity, UserRunWording.InactiveDisabled, "Necto.InactiveDisabled", language);

            if (deletedIds.Count > 0)
            {
                _store.Mutate(c =>
                {
                    foreach (var group in c.Groups)
                    {
                        group.MemberIds.RemoveAll(deletedIds.Contains);
                    }

                    c.ProviderEnrollments.RemoveAll(e => deletedIds.Contains(e.UserId));
                    return true;
                });
            }
        }

        progress?.Report(100);
        return new AccountRuleRun(expired.Count + inactive.Count, deleted.Count);
    }
}
