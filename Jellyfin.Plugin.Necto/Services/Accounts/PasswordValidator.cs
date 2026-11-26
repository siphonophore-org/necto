using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Checks a password against password rules, one rule at a time, so each failure gives its own message.
/// </summary>
public static class PasswordValidator
{
    /// <summary>
    /// Checks a password against password rules.
    /// </summary>
    /// <param name="password">The password chosen.</param>
    /// <param name="rules">The rules the user is held to, or <c>null</c> for none.</param>
    /// <param name="language">The language of the messages, such as de, or <c>null</c> for US English.</param>
    /// <returns>No messages when the password passes, else one message for each rule it breaks.</returns>
    public static IReadOnlyList<string> Validate(string? password, PasswordRules? rules, string? language = null)
    {
        var errors = new List<string>();
        if (rules is not { Enabled: true })
        {
            return errors;
        }

        password ??= string.Empty;

        // An empty password means the account has no password, so the other rules do not apply to it.
        if (password.Length == 0)
        {
            if (rules.DisallowEmpty)
            {
                errors.Add(StolonTranslations.Get(TranslationKeys.ErrorPasswordRequired, language));
            }

            return errors;
        }

        if (password.Length < rules.MinLength)
        {
            errors.Add(StolonTranslations.Format(TranslationKeys.ErrorPasswordTooShort, language, rules.MinLength));
        }

        if (rules.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(StolonTranslations.Get(TranslationKeys.ErrorPasswordNeedsCapital, language));
        }

        if (rules.RequireNumber && !password.Any(char.IsDigit))
        {
            errors.Add(StolonTranslations.Get(TranslationKeys.ErrorPasswordNeedsNumber, language));
        }

        if (rules.RequireSymbol && !password.Any(IsSymbol))
        {
            errors.Add(StolonTranslations.Get(TranslationKeys.ErrorPasswordNeedsSymbol, language));
        }

        return errors;
    }

    private static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
