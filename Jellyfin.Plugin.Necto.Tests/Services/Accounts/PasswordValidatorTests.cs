using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// A password is checked against each rule on its own, so each broken rule gives one message.
/// </summary>
public class PasswordValidatorTests
{
    private static PasswordRules On() => new() { Enabled = true, MinLength = 4 };

    /// <summary>
    /// No rules, or rules turned off, pass any password.
    /// True: a user held to no rules is never refused.
    /// False: a group with rules off would still refuse passwords.
    /// </summary>
    [Fact]
    public void Validate_NoRulesOrRulesOff_Passes()
    {
        Assert.Empty(PasswordValidator.Validate("a", null));
        Assert.Empty(PasswordValidator.Validate("a", new PasswordRules { Enabled = false, MinLength = 50 }));
    }

    /// <summary>
    /// An empty password fails only while empty passwords are disallowed, and then breaks no other rule.
    /// True: an empty password means no password, which a group may allow.
    /// False: an allowed empty password would fail the length rule.
    /// </summary>
    [Fact]
    public void Validate_EmptyPassword_FollowsDisallowEmpty()
    {
        Assert.Equal(new[] { "A password is required." }, PasswordValidator.Validate(string.Empty, On()));
        var allow = On();
        allow.DisallowEmpty = false;
        allow.RequireNumber = true;
        Assert.Empty(PasswordValidator.Validate(null, allow));
    }

    /// <summary>
    /// Each broken rule gives its own message.
    /// True: the user reads every rule their password breaks at once.
    /// False: the user would fix one rule at a time.
    /// </summary>
    [Fact]
    public void Validate_BrokenRules_GiveOneMessageEach()
    {
        var rules = On();
        rules.MinLength = 8;
        rules.RequireUppercase = true;
        rules.RequireNumber = true;
        rules.RequireSymbol = true;

        var errors = PasswordValidator.Validate("abc", rules);

        Assert.Equal(
            new[]
            {
                "Password must be at least 8 characters long.",
                "Password must contain at least one capital letter.",
                "Password must contain at least one number.",
                "Password must contain at least one symbol."
            },
            errors);
    }

    /// <summary>
    /// A password that meets every rule passes.
    /// True: a good password is never refused.
    /// False: a rule would refuse a password that meets it.
    /// </summary>
    [Fact]
    public void Validate_EveryRuleMet_Passes()
    {
        var rules = On();
        rules.RequireUppercase = true;
        rules.RequireNumber = true;
        rules.RequireSymbol = true;

        Assert.Empty(PasswordValidator.Validate("Ab3!", rules));
    }
}
