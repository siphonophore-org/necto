using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using User = Jellyfin.Database.Implementations.Entities.User;
using Jellyfin.Data;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// Makes, changes, and uses invites. Using an invite is the only write anyone may reach without signing in, so every
/// check of the token, PIN, expiry, uses, rate, and group happens here, and the account it makes is never an
/// administrator.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class InviteService : IDisposable
{
    private readonly IConfigurationStore _store;
    private readonly InviteStatusStore _status;
    private readonly IUserManager _userManager;
    private readonly ICryptoProvider _crypto;
    private readonly PasswordEnrollment _enrollment;
    private readonly PolicyTemplateService _templates;
    private readonly ActivityLogger _activity;
    private readonly InviteActivity _summary;
    private readonly INotifier _notifier;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<InviteService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteService"/> class.
    /// </summary>
    /// <param name="store">Reads and saves the invites and the groups.</param>
    /// <param name="status">Keeps each invite's counters.</param>
    /// <param name="userManager">Makes the accounts.</param>
    /// <param name="crypto">Hashes and checks PINs.</param>
    /// <param name="enrollment">Moves a new account onto password rules.</param>
    /// <param name="templates">Writes a new account's group template.</param>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="summary">Sums the wrong PIN entries.</param>
    /// <param name="notifier">Sends the invite events.</param>
    /// <param name="serverConfig">Gives the server's language.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="time">The clock, or <c>null</c> for the system's.</param>
    public InviteService(
        IConfigurationStore store,
        InviteStatusStore status,
        IUserManager userManager,
        ICryptoProvider crypto,
        PasswordEnrollment enrollment,
        PolicyTemplateService templates,
        ActivityLogger activity,
        InviteActivity summary,
        INotifier notifier,
        IServerConfigurationManager serverConfig,
        ILogger<InviteService> logger,
        TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _enrollment = enrollment ?? throw new ArgumentNullException(nameof(enrollment));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    private string? ServerLanguage => StolonTranslations.ServerLanguage(_serverConfig);

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Makes a new token, 192 random bits as hex.
    /// </summary>
    /// <returns>The token.</returns>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>
    /// Works out whether a PIN can be used: empty, or six digits as a Quick Connect code has.
    /// </summary>
    /// <param name="pin">The PIN.</param>
    /// <returns><c>true</c> when it is empty or six digits.</returns>
    public static bool IsValidPin(string? pin)
    {
        var trimmed = pin?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || (trimmed.Length == 6 && trimmed.All(char.IsAsciiDigit));
    }

    /// <summary>
    /// Works out whether a link is an absolute http or https address, so a button can never carry another scheme.
    /// </summary>
    /// <param name="url">The link.</param>
    /// <returns><c>true</c> for an absolute http or https address.</returns>
    public static bool IsValidResourceUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Works out whether an invite's day has come. Expiry counts whole days, so an invite stops on its day.
    /// </summary>
    /// <param name="invite">The invite.</param>
    /// <param name="now">The time now, in UTC.</param>
    /// <returns><c>true</c> when it has an expiry day that has come.</returns>
    public static bool IsExpired(Invite invite, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return invite.ExpiresAt is { } due && due.Date <= now.Date;
    }

    /// <summary>
    /// Works out where an invite stands.
    /// </summary>
    /// <param name="config">The configuration, for the group.</param>
    /// <param name="invite">The invite.</param>
    /// <param name="status">Its counters, or <c>null</c> when it was never used.</param>
    /// <param name="now">The time now, in UTC.</param>
    /// <returns>The state.</returns>
    public static InviteState StateOf(PluginConfiguration config, Invite invite, InviteStatus? status, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(invite);
        if (IsExpired(invite, now))
        {
            return InviteState.Expired;
        }

        if (!invite.Enabled)
        {
            return InviteState.Locked;
        }

        if (invite.MaxUses > 0 && (status?.UsedCount ?? 0) >= invite.MaxUses)
        {
            return InviteState.UsedUp;
        }

        return InviteNormalizer.HasExpired(InviteNormalizer.TargetGroup(config, invite), now) ? InviteState.GroupExpired : InviteState.Active;
    }

    /// <summary>
    /// Lists every invite with its counters, oldest first.
    /// </summary>
    /// <returns>The summaries.</returns>
    public List<InviteSummary> Summaries() => Summaries(_ => true);

    /// <summary>
    /// Lists the invites a member made.
    /// </summary>
    /// <param name="userId">The member.</param>
    /// <returns>The summaries.</returns>
    public List<InviteSummary> SummariesOf(Guid userId) => Summaries(i => i.CreatedBy == userId);

    /// <summary>
    /// Makes an invite.
    /// </summary>
    /// <param name="request">What it holds.</param>
    /// <param name="createdBy">The member who made it, or <see cref="Guid.Empty"/> for the Invites tool.</param>
    /// <param name="language">The language of a refusal.</param>
    /// <returns>The invite, or why it was refused.</returns>
    public (InviteSummary? Invite, string? Error) Create(InviteRequest request, Guid createdBy, string? language)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invite = new Invite { Id = Guid.NewGuid(), Token = NewToken(), CreatedAt = Now, CreatedBy = createdBy, Enabled = true };
        var error = Fill(invite, request, language, keepPin: false);
        if (error is not null)
        {
            return (null, error);
        }

        _store.Mutate(c =>
        {
            c.Invites.Add(invite);
            return true;
        });

        var name = DisplayName(invite);
        _activity.Log(
            StolonTranslations.Get(TranslationKeys.InvitesActivityCreated, ServerLanguage),
            "Necto.InviteCreated",
            ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.InvitesActivityCreatedOverview, ServerLanguage, name)),
            userId: createdBy);
        if (createdBy != Guid.Empty)
        {
            Send(InvitesEvents.Created, TranslationKeys.InvitesActivityCreated, TranslationKeys.InvitesActivityMemberCreatedOverview, null, invite, UserName(createdBy), name);
        }

        return (Summaries(i => i.Id == invite.Id).First(), null);
    }

    /// <summary>
    /// Makes a member's invite, within what their groups allow.
    /// </summary>
    /// <param name="userId">The member.</param>
    /// <param name="request">What it holds.</param>
    /// <param name="language">The language of a refusal.</param>
    /// <returns>The invite, or why it was refused.</returns>
    public (InviteSummary? Invite, string? Error) CreateForMember(Guid userId, InviteRequest request, string? language)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return (null, StolonTranslations.Get(TranslationKeys.InvitesErrorNotAllowed, language));
        }

        var isAdmin = user.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator);
        var (permissions, target, open) = _store.Read(c =>
        {
            var p = PermissionService.Resolve(c, userId, isAdmin);
            var group = InviteNormalizer.TargetGroup(c, new Invite { GroupId = request.GroupId });
            var mine = c.Invites.Count(i => i.CreatedBy == userId && StateOf(c, i, _status.Find(i.Id), Now) == InviteState.Active);
            return (p, group, mine);
        });

        if (!permissions.Invites.MayCreate)
        {
            return (null, StolonTranslations.Get(TranslationKeys.InvitesErrorNotAllowed, language));
        }

        if (permissions.Invites.MaxInvites > 0 && open >= permissions.Invites.MaxInvites)
        {
            return (null, StolonTranslations.Format(TranslationKeys.InvitesErrorLimit, language, permissions.Invites.MaxInvites));
        }

        if (!permissions.MayInviteTo(target.Id))
        {
            return (null, StolonTranslations.Get(TranslationKeys.InvitesErrorGroupNotAllowed, language));
        }

        return Create(request, userId, language);
    }

    /// <summary>
    /// Changes an invite's name, message, links, group, uses, day, and PIN. A PIN left out stays as it is.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="request">What it holds.</param>
    /// <param name="language">The language of a refusal.</param>
    /// <returns>The invite, or why it was refused, or both <c>null</c> when no invite has the id.</returns>
    public (InviteSummary? Invite, string? Error) Update(Guid id, InviteRequest request, string? language)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? error = null;
        var found = false;
        var revived = false;
        _store.Mutate(c =>
        {
            var stored = c.Invites.FirstOrDefault(i => i.Id == id);
            if (stored is null)
            {
                return false;
            }

            found = true;
            var copy = SecretJson.Copy(stored);
            error = Fill(copy, request, language, keepPin: request.Pin is null);
            if (error is not null)
            {
                return false;
            }

            // A day moved ahead turns the invite back on, as moving its expiry alone does.
            if (copy.ExpiresAt != stored.ExpiresAt && (copy.ExpiresAt is not { } due || due.Date > Now.Date))
            {
                copy.Enabled = true;
                revived = true;
            }

            c.Invites[c.Invites.IndexOf(stored)] = copy;
            InviteNormalizer.Normalize(c);
            return true;
        });

        if (!found)
        {
            return (null, null);
        }

        if (revived)
        {
            ClearPinFailures(id);
        }

        return error is not null ? (null, error) : (Summaries(i => i.Id == id).First(), null);
    }

    /// <summary>
    /// Makes a new invite with another invite's settings, PIN included, and a new link.
    /// </summary>
    /// <param name="id">The invite to copy.</param>
    /// <returns>The copy, or <c>null</c> when no invite has the id.</returns>
    public InviteSummary? Duplicate(Guid id)
    {
        Invite? copy = null;
        _store.Mutate(c =>
        {
            var stored = c.Invites.FirstOrDefault(i => i.Id == id);
            if (stored is null)
            {
                return false;
            }

            copy = SecretJson.Copy(stored);
            copy.Id = Guid.NewGuid();
            copy.Token = NewToken();
            copy.CreatedAt = Now;
            copy.Enabled = true;
            c.Invites.Add(copy);
            InviteNormalizer.Normalize(c);
            return true;
        });

        return copy is null ? null : Summaries(i => i.Id == copy.Id).First();
    }

    /// <summary>
    /// Deletes an invite and its counters.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="createdBy">The member it must belong to, or <c>null</c> for any.</param>
    /// <returns><c>true</c> when it was deleted.</returns>
    public bool Delete(Guid id, Guid? createdBy = null)
    {
        var removed = false;
        _store.Mutate(c =>
        {
            removed = c.Invites.RemoveAll(i => i.Id == id && (createdBy is null || i.CreatedBy == createdBy)) > 0;
            return removed;
        });

        if (removed)
        {
            _status.Update(d => d.Invites.Remove(id));
        }

        return removed;
    }

    /// <summary>
    /// Turns an invite on or off. Turning it on clears its wrong PINs, and is refused while its day has passed or its
    /// group disallows password changes.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="enabled">Whether it works.</param>
    /// <returns>What happened.</returns>
    public InviteToggleResult SetEnabled(Guid id, bool enabled)
    {
        var result = InviteToggleResult.NotFound;
        _store.Mutate(c =>
        {
            var invite = c.Invites.FirstOrDefault(i => i.Id == id);
            if (invite is null)
            {
                return false;
            }

            if (enabled && IsExpired(invite, Now))
            {
                result = InviteToggleResult.Expired;
                return false;
            }

            if (enabled && InviteNormalizer.BlocksInvites(InviteNormalizer.TargetGroup(c, invite)))
            {
                result = InviteToggleResult.GroupBlocksInvites;
                return false;
            }

            result = InviteToggleResult.Updated;
            invite.Enabled = enabled;
            return true;
        });

        if (result == InviteToggleResult.Updated && enabled)
        {
            ClearPinFailures(id);
        }

        return result;
    }

    /// <summary>
    /// Moves an invite's day. A day ahead, or none, turns the invite back on and clears its wrong PINs, unless its
    /// group disallows password changes.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="expiresAt">The new day, or <c>null</c> for never.</param>
    /// <returns><c>true</c> when the invite was found.</returns>
    public bool SetExpiry(Guid id, DateTime? expiresAt)
    {
        var found = false;
        var revived = false;
        _store.Mutate(c =>
        {
            var invite = c.Invites.FirstOrDefault(i => i.Id == id);
            if (invite is null)
            {
                return false;
            }

            found = true;
            invite.ExpiresAt = expiresAt?.Date;
            if ((expiresAt is not { } due || due.Date > Now.Date) && !InviteNormalizer.BlocksInvites(InviteNormalizer.TargetGroup(c, invite)))
            {
                invite.Enabled = true;
                revived = true;
            }

            return true;
        });

        if (revived)
        {
            ClearPinFailures(id);
        }

        return found;
    }

    /// <summary>
    /// Turns off every invite whose day has come, and writes one activity entry for them.
    /// </summary>
    /// <returns>How many were turned off.</returns>
    public int ExpireInvites()
    {
        // While Invites is off, no invite changes.
        if (!_store.Read(c => ToolSwitches.IsOn(c, ToolSwitches.Invites)))
        {
            return 0;
        }

        var names = new List<string>();
        _store.Mutate(c =>
        {
            foreach (var invite in c.Invites.Where(i => i.Enabled && IsExpired(i, Now)))
            {
                invite.Enabled = false;
                names.Add(DisplayName(invite));
            }

            return names.Count > 0;
        });

        if (names.Count > 0)
        {
            var language = ServerLanguage;
            var list = string.Join(", ", names);
            var overview = names.Count == 1
                ? StolonTranslations.Format(TranslationKeys.InvitesActivityExpiredOverviewOne, language, list)
                : StolonTranslations.Format(TranslationKeys.InvitesActivityExpiredOverviewMany, language, names.Count, list);
            _activity.Log(
                StolonTranslations.Get(names.Count == 1 ? TranslationKeys.InvitesActivityExpiredOne : TranslationKeys.InvitesActivityExpiredMany, language),
                "Necto.InviteExpired",
                ActivityText.Sentence(overview));
            _ = _notifier.SendAsync(new NectoEvent(InvitesEvents.Expired, StolonTranslations.Get(names.Count == 1 ? TranslationKeys.InvitesActivityExpiredOne : TranslationKeys.InvitesActivityExpiredMany, language), ActivityText.Sentence(overview) ?? string.Empty) { Level = NectoEventLevel.Info });
            _logger.LogInformation("Turned off {Count} expired invites", names.Count);
        }

        return names.Count;
    }

    /// <summary>
    /// Finds an invite by its token, comparing each in fixed time.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <returns>A copy of the invite, or <c>null</c>.</returns>
    public Invite? FindByToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var candidate = Encoding.UTF8.GetBytes(token);
        return _store.Read(c => c.Invites.FirstOrDefault(i => TokenMatches(i.Token, candidate)) is { } match ? SecretJson.Copy(match) : null);
    }

    /// <summary>
    /// Works out whether an invite can make an account now, with its group able to take one.
    /// </summary>
    /// <param name="invite">The invite.</param>
    /// <returns><c>true</c> when it can.</returns>
    public bool IsRedeemableNow(Invite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return _store.Read(c =>
        {
            if (!ToolSwitches.IsOn(c, ToolSwitches.Invites) || StateOf(c, invite, _status.Find(invite.Id), Now) != InviteState.Active)
            {
                return false;
            }

            return !InviteNormalizer.BlocksInvites(InviteNormalizer.TargetGroup(c, invite));
        });
    }

    /// <summary>
    /// Uses an invite to make an account in its group, when every check passes.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="pin">The PIN typed.</param>
    /// <param name="username">The user name to make.</param>
    /// <param name="password">The new account's password.</param>
    /// <param name="language">The language of the answer, or <c>null</c> for US English.</param>
    /// <param name="cancellationToken">Stops the wait for another signup.</param>
    /// <returns>What happened.</returns>
    public async Task<InviteRedeemResult> RedeemAsync(string token, string? pin, string? username, string? password, string? language, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RedeemCoreAsync(token, pin, username, password, language).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<InviteRedeemResult> RedeemCoreAsync(string token, string? pin, string? username, string? password, string? language)
    {
        var invite = FindByToken(token);
        if (invite is null)
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorLinkInvalid, language));
        }

        if (!invite.Enabled || !_store.Read(c => ToolSwitches.IsOn(c, ToolSwitches.Invites)))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorUnavailable, language));
        }

        if (IsExpired(invite, Now))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorExpired, language));
        }

        var status = _status.Find(invite.Id) ?? new InviteStatus();
        if (invite.MaxUses > 0 && status.UsedCount >= invite.MaxUses)
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorUsedUp, language));
        }

        var settings = _store.Read(c => SecretJson.Copy(c.InviteSettings ?? new InviteSettings()));
        if (settings.RateLimitCount > 0 && settings.RateLimitWindowMinutes > 0)
        {
            var cutoff = Now.AddMinutes(-settings.RateLimitWindowMinutes);
            if (status.RecentRedemptions.Count(t => t >= cutoff) >= settings.RateLimitCount)
            {
                return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorUsedRecently, language));
            }
        }

        if (!string.IsNullOrEmpty(invite.PinHash) && !VerifyPin(invite, pin))
        {
            return RecordWrongPin(invite, settings.MaxPinAttempts, language);
        }

        username = username?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorChooseUsername, language));
        }

        if (string.IsNullOrEmpty(password))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorChoosePassword, language));
        }

        var group = _store.Read(c => SecretJson.Copy(InviteNormalizer.TargetGroup(c, invite)));

        // A group switched to disallow password changes after the invite went out is caught here as well.
        if (InviteNormalizer.BlocksInvites(group))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorUnavailable, language));
        }

        // An account in an expired group would be disabled or deleted on the next run, so it is never made.
        if (InviteNormalizer.HasExpired(group, Now))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorGroupExpired, language));
        }

        var errors = PasswordValidator.Validate(password, AccountRuleMerger.Password(new[] { group }), language);
        if (errors.Count > 0)
        {
            return InviteRedeemResult.Fail(string.Join(" ", errors));
        }

        if (_userManager.GetUserByName(username) is not null)
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorSignupRefused, language));
        }

        Guid userId;

        // The scope tells the new user event that this signup places the account itself and holds it disabled until
        // its password is set, and tells the password rule provider the first password is a creation.
        using (InviteRedemptionScope.Begin())
        {
            try
            {
                userId = (await _userManager.CreateUserAsync(username).ConfigureAwait(false)).Id;
            }
            catch (ArgumentException ex)
            {
                _logger.LogDebug(ex, "An invite signup gave a user name Jellyfin refused");
                return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorUsernameNotAllowed, language));
            }

            try
            {
                await WithUserRetryAsync(userId, u => _userManager.ChangePassword(u.Id, password)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not set the password of invited user {UserId}, so the account is removed", userId);
                await RollBackAsync(userId).ConfigureAwait(false);
                return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorCreateAccount, language));
            }
        }

        // Everything after creation finishes or removes the account, so a half made account never keeps the name.
        try
        {
            await SetUpAccountAsync(group, userId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not finish setting up invited user {UserId}, so the account is removed", userId);
            await RollBackAsync(userId).ConfigureAwait(false);
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorCreateAccount, language));
        }

        var used = RecordRedemption(invite.Id, settings.RateLimitWindowMinutes);
        LogRedeemed(invite, group, username, userId, used);

        var resources = invite.Resources.Where(r => !string.IsNullOrWhiteSpace(r.Title) && IsValidResourceUrl(r.Url)).ToList();
        return new InviteRedeemResult(true, StolonTranslations.Get(TranslationKeys.InvitesMessageAccountCreated, language), resources);
    }

    private void LogRedeemed(Invite invite, Group group, string username, Guid userId, int used)
    {
        var language = ServerLanguage;
        var name = DisplayName(invite);
        var groupName = group.IsEveryone ? StolonTranslations.Get(TranslationKeys.GroupEveryone, language) : group.Name;
        _logger.LogInformation("Invite {InviteId} made user {UserId}", invite.Id, userId);
        _activity.Log(
            StolonTranslations.Get(TranslationKeys.InvitesActivityUsed, language),
            "Necto.InviteUsed",
            ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.InvitesActivityUsedOverview, language, username, name, groupName)),
            userId: userId);
        Send(InvitesEvents.Used, TranslationKeys.InvitesActivityUsed, TranslationKeys.InvitesActivityUsedOverview, NectoEventLevel.Info, invite, username, name, groupName);

        if (invite.MaxUses > 0 && used >= invite.MaxUses)
        {
            var overview = invite.MaxUses == 1
                ? StolonTranslations.Format(TranslationKeys.InvitesActivityUsedUpOverviewOne, language, name)
                : StolonTranslations.Format(TranslationKeys.InvitesActivityUsedUpOverviewMany, language, name, invite.MaxUses);
            _activity.Log(StolonTranslations.Get(TranslationKeys.InvitesActivityUsedUp, language), "Necto.InviteUsedUp", ActivityText.Sentence(overview));
            _ = _notifier.SendAsync(Event(InvitesEvents.UsedUp, StolonTranslations.Get(TranslationKeys.InvitesActivityUsedUp, language), ActivityText.Sentence(overview) ?? string.Empty, NectoEventLevel.Info, invite));
        }
    }

    private InviteRedeemResult RecordWrongPin(Invite invite, int maxAttempts, string? language)
    {
        var failed = 0;
        _status.Update(d =>
        {
            failed = ++StatusFor(d, invite.Id).FailedPinAttempts;
            return true;
        });

        _summary.Add(InviteActivity.PinFailed, DisplayName(invite));
        if (failed < Math.Max(1, maxAttempts))
        {
            return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorIncorrectPin, language));
        }

        // Anyone with the link can guess, so a run of wrong PINs locks the invite until an administrator turns it on.
        _store.Mutate(c =>
        {
            var stored = c.Invites.FirstOrDefault(i => i.Id == invite.Id);
            if (stored is null || !stored.Enabled)
            {
                return false;
            }

            stored.Enabled = false;
            return true;
        });

        var serverLanguage = ServerLanguage;
        var name = DisplayName(invite);
        var overview = failed == 1
            ? StolonTranslations.Format(TranslationKeys.InvitesActivityLockedOverviewOne, serverLanguage, name)
            : StolonTranslations.Format(TranslationKeys.InvitesActivityLockedOverviewMany, serverLanguage, name, failed);
        _logger.LogWarning("Invite {InviteId} locked after {Count} wrong PINs", invite.Id, failed);
        _activity.Log(StolonTranslations.Get(TranslationKeys.InvitesActivityLocked, serverLanguage), "Necto.InviteLocked", ActivityText.Sentence(overview), LogLevel.Warning);
        _ = _notifier.SendAsync(Event(InvitesEvents.Locked, StolonTranslations.Get(TranslationKeys.InvitesActivityLocked, serverLanguage), ActivityText.Sentence(overview) ?? string.Empty, NectoEventLevel.Warning, invite));
        return InviteRedeemResult.Fail(StolonTranslations.Get(TranslationKeys.InvitesErrorLocked, language));
    }

    private int RecordRedemption(Guid inviteId, int windowMinutes)
    {
        var used = 0;
        var valid = _store.Read(c => c.Invites.Select(i => i.Id).ToHashSet());
        _status.Update(d =>
        {
            // Counters of deleted invites go in the same write, so the file never grows without end.
            foreach (var key in d.Invites.Keys.Where(k => !valid.Contains(k)).ToList())
            {
                d.Invites.Remove(key);
            }

            if (!valid.Contains(inviteId))
            {
                return true;
            }

            var status = StatusFor(d, inviteId);
            status.UsedCount++;
            status.FailedPinAttempts = 0;
            if (windowMinutes > 0)
            {
                var cutoff = Now.AddMinutes(-windowMinutes);
                status.RecentRedemptions.RemoveAll(t => t < cutoff);
                status.RecentRedemptions.Add(Now);
            }

            used = status.UsedCount;
            return true;
        });

        return used;
    }

    private async Task SetUpAccountAsync(Group group, Guid userId)
    {
        if (!group.IsEveryone)
        {
            _store.Mutate(c =>
            {
                var stored = c.Groups.FirstOrDefault(g => g.Id == group.Id);
                if (stored is null || stored.MemberIds.Contains(userId))
                {
                    return false;
                }

                stored.MemberIds.Add(userId);
                return true;
            });
        }

        await WithUserRetryAsync(userId, async u => await _enrollment.ReconcileUserAsync(u).ConfigureAwait(false)).ConfigureAwait(false);
        await WithUserRetryAsync(userId, async u => await _templates.ApplyAsync(u, joined: true).ConfigureAwait(false)).ConfigureAwait(false);

        // The hold ends only now, with the password set and the group applied. A template that disables accounts keeps it.
        var template = _store.Read(c => PolicyTemplateMerger.For(c, userId));
        var keepDisabled = template is { Access.ManageIsDisabled: true, Access.IsDisabled: true };
        await WithUserRetryAsync(userId, async u =>
        {
            var policy = _userManager.GetUserDto(u, string.Empty).Policy;
            if (policy.IsAdministrator || policy.IsDisabled != keepDisabled)
            {
                policy.IsAdministrator = false;
                policy.IsDisabled = keepDisabled;
                await _userManager.UpdatePolicyAsync(u.Id, policy).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private async Task RollBackAsync(Guid userId)
    {
        try
        {
            await _userManager.DeleteUserAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not delete half made user {UserId}", userId);
        }

        _store.Mutate(c =>
        {
            var removed = c.Groups.Sum(g => g.MemberIds.RemoveAll(id => id == userId));
            removed += c.ProviderEnrollments.RemoveAll(e => e.UserId == userId);
            return removed > 0;
        });
    }

    private async Task WithUserRetryAsync(Guid userId, Func<User, Task> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            var user = _userManager.GetUserById(userId);
            if (user is null)
            {
                return;
            }

            try
            {
                await action(user).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < 5 && IsConcurrency(ex))
            {
                _logger.LogDebug("User {UserId} changed under a write, try {Attempt}", userId, attempt);
                await Task.Delay(75 * attempt).ConfigureAwait(false);
            }
        }
    }

    private static bool IsConcurrency(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name.Contains("Concurrency", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool VerifyPin(Invite invite, string? pin)
    {
        try
        {
            return _crypto.Verify(PasswordHash.Parse(invite.PinHash), (pin ?? string.Empty).Trim());
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private void ClearPinFailures(Guid id)
        => _status.Update(d =>
        {
            if (!d.Invites.TryGetValue(id, out var status) || status.FailedPinAttempts == 0)
            {
                return false;
            }

            status.FailedPinAttempts = 0;
            return true;
        });

    private string? Fill(Invite invite, InviteRequest request, string? language, bool keepPin)
    {
        if (!keepPin && !IsValidPin(request.Pin))
        {
            return StolonTranslations.Get(TranslationKeys.InvitesErrorPin, language);
        }

        var resources = new List<InviteResource>();
        foreach (var resource in request.Resources ?? new List<InviteResource>())
        {
            var title = resource?.Title?.Trim() ?? string.Empty;
            if (resource is null || title.Length == 0 || !IsValidResourceUrl(resource.Url))
            {
                return StolonTranslations.Get(TranslationKeys.InvitesErrorResource, language);
            }

            resources.Add(new InviteResource { Title = title, Url = resource.Url.Trim() });
        }

        var error = _store.Read(c =>
        {
            if (request.GroupId is { } gid && gid != Group.EveryoneId && c.Groups.All(g => g.Id != gid))
            {
                return StolonTranslations.Get(TranslationKeys.InvitesErrorGroupMissing, language);
            }

            var target = InviteNormalizer.TargetGroup(c, new Invite { GroupId = request.GroupId });
            return InviteNormalizer.BlocksInvites(target) ? StolonTranslations.Get(TranslationKeys.InvitesErrorGroupBlocks, language) : null;
        });
        if (error is not null)
        {
            return error;
        }

        invite.Label = (request.Label ?? string.Empty).Trim();
        invite.Message = (request.Message ?? string.Empty).Trim();
        invite.Resources = resources;
        invite.GroupId = request.GroupId;
        invite.ExpiresAt = request.ExpiresAt?.Date;
        invite.MaxUses = Math.Max(0, request.MaxUses);
        if (!keepPin)
        {
            var pin = request.Pin?.Trim() ?? string.Empty;
            invite.PinHash = pin.Length == 0 ? string.Empty : _crypto.CreatePasswordHash(pin).ToString();
        }

        return null;
    }

    private List<InviteSummary> Summaries(Func<Invite, bool> which)
    {
        var status = _status.Load();
        var now = Now;
        var rows = _store.Read(c => c.Invites.Where(which).Select(i =>
        {
            var counters = status.Invites.GetValueOrDefault(i.Id);
            var group = InviteNormalizer.TargetGroup(c, i);
            return new InviteSummary
            {
                Id = i.Id,
                Token = i.Token,
                Label = i.Label,
                Message = i.Message,
                Resources = i.Resources.Select(r => new InviteResource { Title = r.Title, Url = r.Url }).ToList(),
                HasPin = !string.IsNullOrEmpty(i.PinHash),
                GroupId = i.GroupId,
                GroupName = group.IsEveryone ? StolonTranslations.Get(TranslationKeys.GroupEveryone, ServerLanguage) : group.Name,
                ExpiresAt = i.ExpiresAt,
                MaxUses = i.MaxUses,
                UsedCount = counters?.UsedCount ?? 0,
                Enabled = i.Enabled,
                State = StateOf(c, i, counters, now),
                CreatedAt = i.CreatedAt,
                CreatedBy = i.CreatedBy
            };
        }).OrderBy(i => i.CreatedAt).ToList());

        foreach (var row in rows.Where(r => r.CreatedBy != Guid.Empty))
        {
            row.CreatedByName = UserName(row.CreatedBy);
        }

        return rows;
    }

    private string UserName(Guid id) => _userManager.GetUserById(id)?.Username ?? string.Empty;

    private string DisplayName(Invite invite) => invite.Label.Length > 0 ? invite.Label : StolonTranslations.Get(TranslationKeys.InvitesUntitled, ServerLanguage);

    private void Send(string key, string titleKey, string bodyKey, NectoEventLevel? level, Invite invite, params object[] values)
    {
        var language = ServerLanguage;
        _ = _notifier.SendAsync(Event(key, StolonTranslations.Get(titleKey, language), ActivityText.Sentence(StolonTranslations.Format(bodyKey, language, values)) ?? string.Empty, level ?? NectoEventLevel.Info, invite));
    }

    private static NectoEvent Event(string key, string title, string body, NectoEventLevel level, Invite invite) => new(key, title, body)
    {
        UserId = invite.CreatedBy == Guid.Empty ? null : invite.CreatedBy,
        Level = level,
        Url = invite.CreatedBy == Guid.Empty ? "web/#/configurationpage?name=necto" : "invites",
        Data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inviteId"] = invite.Id.ToString("N", CultureInfo.InvariantCulture),
            ["invite"] = invite.Label
        }
    };

    private static InviteStatus StatusFor(InviteStatusData data, Guid id)
        => data.Invites.TryGetValue(id, out var status) ? status : (data.Invites[id] = new InviteStatus());

    private static bool TokenMatches(string? stored, byte[] candidate)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(stored);
        return bytes.Length == candidate.Length && CryptographicOperations.FixedTimeEquals(bytes, candidate);
    }
}
