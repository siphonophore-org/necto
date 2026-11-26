using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Model.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// The password rule provider holds a member changing their own password to their groups' rules, answers a broken
/// rule with an ArgumentException so the form shows it, and lets administrators, internal work, and anonymous rescues
/// through.
/// </summary>
public class PasswordRuleAuthenticationProviderTests
{
    private readonly FakeConfigurationStore _config = new();
    private readonly FakeActivity _activity = new();
    private readonly FakeUsers _users = new();

    private sealed class FakeCrypto : ICryptoProvider
    {
        public string DefaultHashMethod => "Test";

        public PasswordHash CreatePasswordHash(ReadOnlySpan<char> password) => new("Test", new byte[] { 1, 2, 3 });

        public bool Verify(PasswordHash hash, ReadOnlySpan<char> password) => password.SequenceEqual("right");

        public byte[] GenerateSalt() => new byte[] { 1 };

        public byte[] GenerateSalt(int length) => new byte[length];
    }

    private sealed class Accessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static IHttpContextAccessor Caller(bool signedIn, bool admin = false)
    {
        var claims = admin ? new[] { new Claim(ClaimTypes.Role, "Administrator") } : Array.Empty<Claim>();
        var identity = signedIn ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity();
        return new Accessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
    }

    private PasswordRuleAuthenticationProvider Provider(IHttpContextAccessor? caller)
        => new(new FakeCrypto(), _config, _activity.Logger, FakeServerConfig.Create(), NullLogger<PasswordRuleAuthenticationProvider>.Instance, caller);

    private User Member(Action<PasswordRules> rules, string? password = "hash")
    {
        var user = _users.Add("viewer");
        user.Password = password;
        _config.Configuration.Groups.Add(TestGroups.Make("Family", p => { p.Accounts.Password.Enabled = true; p.Accounts.Password.MinLength = 8; rules(p.Accounts.Password); }, user.Id));
        return user;
    }

    /// <summary>
    /// A member's password that breaks a rule is refused with an ArgumentException naming the rules.
    /// True: Jellyfin answers 400 and the password form shows why.
    /// False: a 401 would read as a lost session and leave the form spinning.
    /// </summary>
    [Fact]
    public async Task ChangePassword_BrokenRule_ThrowsArgumentExceptionWithReasons()
    {
        var user = Member(r => r.RequireNumber = true);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Provider(Caller(true)).ChangePassword(user, "short"));

        Assert.Contains("at least 8 characters", error.Message, StringComparison.Ordinal);
        Assert.Contains("at least one number", error.Message, StringComparison.Ordinal);
        Assert.Equal("hash", user.Password);
        Assert.Contains("Password change rejected", _activity.Names);
    }

    /// <summary>
    /// A password that meets the rules is hashed and set.
    /// True: a member with a good password changes it.
    /// False: the rules would block every change.
    /// </summary>
    [Fact]
    public async Task ChangePassword_MeetsRules_SetsTheHash()
    {
        var user = Member(_ => { });

        await Provider(Caller(true)).ChangePassword(user, "longenough");

        Assert.Equal(new PasswordHash("Test", new byte[] { 1, 2, 3 }).ToString(), user.Password);
    }

    /// <summary>
    /// A member of an enforcing group and a free group is still held to the rules.
    /// True: password rules from any group hold.
    /// False: joining a second group would lift the rules.
    /// </summary>
    [Fact]
    public async Task ChangePassword_SecondGroupWithoutRules_StillHoldsTheRules()
    {
        var user = Member(_ => { });
        _config.Configuration.Groups.Add(TestGroups.Make("Free", user.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => Provider(Caller(true)).ChangePassword(user, "short"));
    }

    /// <summary>
    /// A user in no enforcing group changes their password freely.
    /// True: rules hold only where a group sets them.
    /// False: every user on the provider would be held to rules.
    /// </summary>
    [Fact]
    public async Task ChangePassword_NoRules_IsFree()
    {
        var user = _users.Add("viewer");

        await Provider(Caller(true)).ChangePassword(user, "a");

        Assert.NotNull(user.Password);
    }

    /// <summary>
    /// Initial password only blocks a change but lets a member with no password set one.
    /// True: a member sets their first password, then only an administrator changes it.
    /// False: the mode would block the first password, or allow later changes.
    /// </summary>
    [Fact]
    public async Task ChangePassword_InitialOnly_BlocksChangesButAllowsTheFirst()
    {
        var user = Member(r => r.ChangeMode = PasswordChangeMode.InitialOnly);
        await Assert.ThrowsAsync<ArgumentException>(() => Provider(Caller(true)).ChangePassword(user, "longenough"));
        Assert.Contains("Password change blocked", _activity.Names);

        user.Password = null;
        await Provider(Caller(true)).ChangePassword(user, "longenough");
        Assert.NotNull(user.Password);
    }

    /// <summary>
    /// Disallowed blocks a member's first password too.
    /// True: only an administrator sets a password for such a member.
    /// False: a member could set a password their group forbids.
    /// </summary>
    [Fact]
    public async Task ChangePassword_Disallowed_BlocksTheFirstPassword()
    {
        var user = Member(r => r.ChangeMode = PasswordChangeMode.Disallowed, password: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Provider(Caller(true)).ChangePassword(user, "longenough"));

        Assert.StartsWith("Password changes are disabled", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An administrator, internal work with no request, and an anonymous rescue all pass, empty passwords included.
    /// True: an administrator can always reset a member, and the forgot password flow works.
    /// False: a member locked out by rules they cannot meet would have no way back in.
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("internal")]
    [InlineData("anonymous")]
    public async Task ChangePassword_NotSelfService_BypassesTheRules(string caller)
    {
        var user = Member(r => r.ChangeMode = PasswordChangeMode.Disallowed);
        var accessor = caller switch
        {
            "admin" => Caller(true, admin: true),
            "anonymous" => Caller(false),
            _ => null
        };

        await Provider(accessor).ChangePassword(user, string.Empty);

        Assert.Null(user.Password);
    }

    /// <summary>
    /// Signing in checks the stored hash as Jellyfin's own provider does, and an unknown name is refused.
    /// True: moving onto the provider changes nothing about signing in.
    /// False: a typo in a username would answer 500 for the whole sign in.
    /// </summary>
    [Fact]
    public async Task Authenticate_ChecksTheHash_AndRefusesUnknownUsers()
    {
        var user = _users.Add("viewer");
        user.Password = new PasswordHash("Test", new byte[] { 1 }).ToString();
        var provider = Provider(null);

        var result = await provider.Authenticate("viewer", "right", user);

        Assert.Equal("viewer", result.Username);
        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("viewer", "wrong", user));
        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("nobody", "right", null));
    }
}
