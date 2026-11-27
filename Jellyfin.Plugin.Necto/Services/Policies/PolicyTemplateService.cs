using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Writes the template a user takes onto their Jellyfin policy, settings, and web client home sections.
/// Administrators are never changed. A template that turns accounts on never turns on one that expiry or inactivity
/// turned off.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class PolicyTemplateService
{
    // The web client keeps its home sections in the display preferences of this item, for the emby client.
    private static readonly Guid HomeSettingsItemId = Guid.Parse("3CE5B65D-E116-D731-65D1-EFC4A30EC35C");
    private const string HomeSettingsClient = "emby";

    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly IDisplayPreferencesManager? _displayPreferences;
    private readonly ILogger<PolicyTemplateService> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyTemplateService"/> class.
    /// </summary>
    /// <param name="store">Reads the groups.</param>
    /// <param name="userManager">Reads and writes the users' policies and settings.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="displayPreferences">Writes the web client's home sections, or <c>null</c> to leave them.</param>
    /// <param name="time">The clock, or <c>null</c> for the system's.</param>
    public PolicyTemplateService(IConfigurationStore store, IUserManager userManager, ILogger<PolicyTemplateService> logger, IDisplayPreferencesManager? displayPreferences = null, TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _displayPreferences = displayPreferences;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Writes the template a user takes onto them.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="joined">Whether the user just joined their group, so preferences written on join only are written too.</param>
    /// <returns><c>true</c> when a template was written, <c>false</c> for an administrator or a user with none.</returns>
    public async Task<bool> ApplyAsync(User user, bool joined = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.HasPermission(PermissionKind.IsAdministrator))
        {
            return false;
        }

        var (template, accounts) = _store.Read(c => (PolicyTemplateMerger.For(c, user.Id), PermissionService.Resolve(c, user.Id, false).Accounts));
        if (!PolicyTemplateRows.SetsAnything(template))
        {
            return false;
        }

        var dto = _userManager.GetUserDto(user, string.Empty);
        var policy = dto.Policy;
        template.Access.ApplyTo(policy, user.Id);
        policy.IsAdministrator = false;
        if (template.Access.ManageIsDisabled && !template.Access.IsDisabled && HeldByAccountRules(dto, accounts, _time.GetUtcNow().UtcDateTime))
        {
            policy.IsDisabled = true;
        }

        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);

        // A group that writes preferences on join only leaves a member's own choices alone after that.
        var writePreferences = joined || template.PreferencesMode == PreferencesMode.Maintain;
        if (writePreferences && template.Preferences.ManagesAnything())
        {
            var settings = dto.Configuration;
            template.Preferences.ApplyTo(settings);
            await _userManager.UpdateConfigurationAsync(user.Id, settings).ConfigureAwait(false);
        }

        if (writePreferences && template.Preferences.ManageHomeSections)
        {
            ApplyHomeSections(user.Id, template.Preferences.HomeSections);
        }

        return true;
    }

    /// <summary>
    /// Writes each user's template onto them, such as after a change to the groups or to repair a change made in the
    /// dashboard.
    /// </summary>
    /// <param name="progress">Takes the progress, from 0 to 100.</param>
    /// <param name="cancellationToken">Stops the run between users.</param>
    /// <param name="joined">The users who just joined their group, who take preferences written on join only. [optional]</param>
    /// <returns>How many users took a template.</returns>
    public async Task<int> ApplyAllAsync(IProgress<double>? progress, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? joined = null)
    {
        var users = _userManager.GetUsers().Where(u => !u.HasPermission(PermissionKind.IsAdministrator)).ToList();
        var applied = 0;
        for (var i = 0; i < users.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ApplyAsync(users[i], joined?.Contains(users[i].Id) == true).ConfigureAwait(false))
                {
                    applied++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not write the template onto user {UserId}", users[i].Id);
            }

            progress?.Report((i + 1) * 100.0 / users.Count);
        }

        progress?.Report(100);
        return applied;
    }

    /// <summary>
    /// Works out whether expiry or inactivity keeps a user disabled, so a template that turns accounts on leaves them off.
    /// </summary>
    /// <param name="dto">The user, with their last activity.</param>
    /// <param name="accounts">The account rules that hold for the user.</param>
    /// <param name="now">The time now, in UTC.</param>
    /// <returns><c>true</c> when the user's groups have expired or the user has been inactive past the window.</returns>
    public static bool HeldByAccountRules(UserDto dto, EffectiveAccounts accounts, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.ExpiresOn is { } due && due.Date <= now.Date)
        {
            return true;
        }

        var last = dto.LastActivityDate ?? dto.LastLoginDate;
        return accounts.InactiveDays is { } days && days > 0 && last is { } seen && seen < now.AddDays(-days);
    }

    private void ApplyHomeSections(Guid userId, List<string> sections)
    {
        if (_displayPreferences is null)
        {
            return;
        }

        try
        {
            var prefs = _displayPreferences.GetDisplayPreferences(userId, HomeSettingsItemId, HomeSettingsClient);
            prefs.HomeSections.Clear();
            for (var i = 0; i < sections.Count; i++)
            {
                if (Enum.TryParse<HomeSectionType>(sections[i], ignoreCase: true, out var type))
                {
                    prefs.HomeSections.Add(new HomeSection { Order = i, Type = type });
                }
            }

            _displayPreferences.UpdateDisplayPreferences(prefs);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not write the home sections of user {UserId}", userId);
        }
    }
}
