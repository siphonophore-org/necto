using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// The authentication provider for users held to password rules. It signs users in exactly as Jellyfin's own provider
/// does, and checks a new password a member sets for themselves against the rules of their groups. A change an
/// administrator makes, or one Jellyfin makes with no signed in caller, such as a forgotten password, always passes.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IAuthenticationProvider))]
public sealed class PasswordRuleAuthenticationProvider : IAuthenticationProvider, IRequiresResolvedUser
{
    private readonly ICryptoProvider _cryptoProvider;
    private readonly IConfigurationStore _store;
    private readonly ActivityLogger _activity;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<PasswordRuleAuthenticationProvider> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordRuleAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="cryptoProvider">Checks and hashes passwords as Jellyfin's own provider does.</param>
    /// <param name="store">Reads the groups and their password rules.</param>
    /// <param name="activity">Writes an entry for each refused change.</param>
    /// <param name="serverConfig">Gives the server's language for the messages.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="httpContextAccessor">Tells who asked for the change.</param>
    public PasswordRuleAuthenticationProvider(
        ICryptoProvider cryptoProvider,
        IConfigurationStore store,
        ActivityLogger activity,
        IServerConfigurationManager serverConfig,
        ILogger<PasswordRuleAuthenticationProvider> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentNullException.ThrowIfNull(logger);
        _cryptoProvider = cryptoProvider;
        _store = store;
        _activity = activity;
        _serverConfig = serverConfig;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string Name => "Necto password rules";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        => throw new AuthenticationException("Invalid username or password.");

    /// <inheritdoc />
    public Task<ProviderAuthenticationResult> Authenticate(string username, string password, User? resolvedUser)
    {
        // Jellyfin asks every provider when a name matches no account, and only catches AuthenticationException.
        if (resolvedUser is null)
        {
            throw new AuthenticationException("Invalid username or password.");
        }

        var success = string.IsNullOrEmpty(resolvedUser.Password)
            ? string.IsNullOrEmpty(password)
            : _cryptoProvider.Verify(PasswordHash.Parse(resolvedUser.Password), password);

        if (!success)
        {
            throw new AuthenticationException("Invalid username or password.");
        }

        return Task.FromResult(new ProviderAuthenticationResult { Username = resolvedUser.Username });
    }

    /// <inheritdoc />
    public Task ChangePassword(User user, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        var language = StolonTranslations.ServerLanguage(_serverConfig);

        // Rules hold only a member changing their own password. Every other path is a rescue or a creation with its own check.
        if (!user.HasPermission(PermissionKind.IsAdministrator) && CallerIsSelfService() && !Invites.InviteRedemptionScope.IsActive)
        {
            var rules = _store.Read(c => AccountRuleMerger.Password(GroupMembership.GroupsOf(c, user.Id)));
            if (rules is not null && rules.ChangeMode != PasswordChangeMode.Allowed)
            {
                var settingFirst = string.IsNullOrEmpty(user.Password);
                if (!(settingFirst && rules.ChangeMode == PasswordChangeMode.InitialOnly))
                {
                    _logger.LogDebug("Refused a password change for {UserId}, whose groups disallow it", user.Id);
                    _activity.Log(
                        StolonTranslations.Get(TranslationKeys.ActivityPasswordChangeBlocked, language),
                        "Necto.PasswordChangeBlocked",
                        ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.ActivityPasswordChangeBlockedOverview, language, user.Username)),
                        LogLevel.Warning,
                        user.Id);

                    // ArgumentException answers 400, which the password form shows. 401 would read as a lost session.
                    throw new ArgumentException(StolonTranslations.Get(TranslationKeys.ErrorPasswordChangesDisabled, language));
                }
            }

            var errors = PasswordValidator.Validate(newPassword, rules, language);
            if (errors.Count > 0)
            {
                _logger.LogDebug("Refused a password change for {UserId} that breaks its rules", user.Id);
                _activity.Log(
                    StolonTranslations.Get(TranslationKeys.ActivityPasswordChangeRejected, language),
                    "Necto.PasswordChangeRejected",
                    RejectedOverview(user.Username, errors, language),
                    LogLevel.Warning,
                    user.Id);
                throw new ArgumentException(string.Join(" ", errors));
            }
        }

        user.Password = string.IsNullOrEmpty(newPassword) ? null : _cryptoProvider.CreatePasswordHash(newPassword).ToString();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the overview of a refused change, naming the user and the rules the password broke.
    /// </summary>
    /// <param name="username">The user who chose the password.</param>
    /// <param name="errors">The messages of the broken rules.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string RejectedOverview(string username, IReadOnlyList<string> errors, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return ActivityText.Sentence(StolonTranslations.Format(
            TranslationKeys.ActivityPasswordChangeRejectedOverview,
            language,
            username,
            string.Join(", ", errors.Select(e => e.Trim().TrimEnd('.'))))) ?? string.Empty;
    }

    /// <summary>
    /// Works out whether a signed in member is changing their own password. No request means work inside the server,
    /// and an anonymous request is a rescue that Jellyfin checks itself.
    /// </summary>
    private bool CallerIsSelfService()
    {
        var principal = _httpContextAccessor?.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true && !principal.IsInRole("Administrator");
    }
}
