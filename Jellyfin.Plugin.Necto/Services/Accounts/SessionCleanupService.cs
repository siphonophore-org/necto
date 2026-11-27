using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Queries;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Logs out users' devices that went too long without checking in, by the session rules of their groups. A device is
/// logged out only when every group cleans its client, after the largest window they give. Administrators are never
/// touched.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class SessionCleanupService
{
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly IDeviceManager _deviceManager;
    private readonly ISessionManager _sessionManager;
    private readonly ActivityLogger _activity;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionCleanupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionCleanupService"/> class.
    /// </summary>
    /// <param name="store">Reads the groups.</param>
    /// <param name="userManager">Lists the users.</param>
    /// <param name="deviceManager">Lists each user's devices.</param>
    /// <param name="sessionManager">Logs a device out.</param>
    /// <param name="activity">Writes one entry for a run that logged out devices.</param>
    /// <param name="serverConfig">Gives the server's language for the entry.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="time">Gives the time now. Left out, it is the system clock.</param>
    public SessionCleanupService(
        IConfigurationStore store,
        IUserManager userManager,
        IDeviceManager deviceManager,
        ISessionManager sessionManager,
        ActivityLogger activity,
        IServerConfigurationManager serverConfig,
        ILogger<SessionCleanupService> logger,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(deviceManager);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _userManager = userManager;
        _deviceManager = deviceManager;
        _sessionManager = sessionManager;
        _activity = activity;
        _serverConfig = serverConfig;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Logs out every stale device of every user whose groups clean up sessions.
    /// </summary>
    /// <param name="progress">Hears how far the run is, from 0 to 100.</param>
    /// <param name="cancellationToken">Stops the run between users.</param>
    /// <returns>How many devices were logged out.</returns>
    public async Task<int> CleanupAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var config = _store.Read(c => Siphonophore.Stolon.SecretJson.Copy(c));
        var users = _userManager.GetUsers().Where(u => !u.HasPermission(PermissionKind.IsAdministrator)).ToList();
        var language = StolonTranslations.ServerLanguage(_serverConfig);
        var unknownApp = StolonTranslations.Get(TranslationKeys.UnknownApp, language);

        var removed = 0;
        var byUser = new Dictionary<Guid, (string Username, Dictionary<string, int> Apps)>();
        for (var i = 0; i < users.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = users[i];
            var group = GroupMembership.GroupOf(config, user.Id);
            if (!AccountRuleMerger.CleansSessions(group))
            {
                progress?.Report((i + 1) * 100.0 / users.Count);
                continue;
            }

            foreach (var device in _deviceManager.GetDevices(new DeviceQuery { UserId = user.Id }).Items)
            {
                if (AccountRuleMerger.SessionDays(group, device.AppName) is not { } days || device.DateLastActivity >= now.AddDays(-days))
                {
                    continue;
                }

                try
                {
                    await _sessionManager.Logout(device).ConfigureAwait(false);
                    removed++;
                    var app = string.IsNullOrWhiteSpace(device.AppName) ? unknownApp : device.AppName;
                    if (!byUser.TryGetValue(user.Id, out var counted))
                    {
                        counted = (user.Username, new Dictionary<string, int>(StringComparer.Ordinal));
                        byUser[user.Id] = counted;
                    }

                    counted.Apps[app] = counted.Apps.GetValueOrDefault(app) + 1;
                    _logger.LogDebug("Logged out a stale {AppName} device of {UserId}", FormatUtilities.SanitizeForLog(device.AppName), user.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not log out device {DeviceId} of {UserId}", FormatUtilities.SanitizeForLog(device.DeviceId), user.Id);
                }
            }

            progress?.Report((i + 1) * 100.0 / users.Count);
        }

        if (removed > 0)
        {
            _activity.Log(
                StolonTranslations.Get(removed == 1 ? TranslationKeys.ActivitySessionsLoggedOutOne : TranslationKeys.ActivitySessionsLoggedOutMany, language),
                "Necto.SessionsLoggedOut",
                CleanupOverview(byUser.Values.ToDictionary(u => u.Username, u => (IReadOnlyDictionary<string, int>)u.Apps, StringComparer.Ordinal), language),
                userId: byUser.Count == 1 ? byUser.Keys.First() : default);
        }

        progress?.Report(100);
        return removed;
    }

    /// <summary>
    /// Builds the overview of a run's entry, such as "Logged out viewer's stale session on Jellyfin Web" for one session,
    /// or "Logged out 3 stale sessions for viewer on Infuse (2) and guest on Swiftfin" for more.
    /// </summary>
    /// <param name="removedByUser">How many sessions each user lost on each client.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string CleanupOverview(IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> removedByUser, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(removedByUser);
        var users = removedByUser
            .Select(u => (Username: u.Key, Apps: u.Value, Total: u.Value.Values.Where(v => v > 0).Sum()))
            .Where(u => u.Total > 0)
            .OrderByDescending(u => u.Total)
            .ThenBy(u => u.Username, StringComparer.Ordinal)
            .ToList();
        var total = users.Sum(u => u.Total);
        if (total == 1)
        {
            return ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.ActivitySessionsLoggedOutOverviewOne, language, users[0].Username, Counted(users[0].Apps, true))) ?? string.Empty;
        }

        var parts = users.Select(u => StolonTranslations.Format(TranslationKeys.ActivitySessionsUserApps, language, u.Username, Counted(u.Apps, users.Count == 1))).ToList();
        var joined = parts.Count == 1
            ? parts[0]
            : StolonTranslations.Format(TranslationKeys.ActivityListAnd, language, string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]);
        return ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.ActivitySessionsLoggedOutOverviewMany, language, total, joined)) ?? string.Empty;
    }

    /// <summary>
    /// Lists clients with their counts, the largest first, such as "Infuse (2), Swiftfin". A count of 1 is left out, and so
    /// is the count of a lone client when the overview states the total.
    /// </summary>
    private static string Counted(IReadOnlyDictionary<string, int> counts, bool dropSingle)
    {
        var entries = counts.Where(p => p.Value > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        var bare = dropSingle && entries.Count == 1;
        return string.Join(", ", entries.Select(p => bare || p.Value == 1 ? p.Key : string.Create(CultureInfo.InvariantCulture, $"{p.Key} ({p.Value})")));
    }
}
