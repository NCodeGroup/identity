// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Authentication.Interactive;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Composes the plain-text mail that carries a one-time code, in the wording of an <see cref="InteractiveLocale"/>.
/// </summary>
// See ADR-1533.
internal static class LocalAccountMailComposer
{
    /// <summary>
    /// Composes the mail of a code.
    /// </summary>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="to">The address the mail is sent to.</param>
    /// <param name="code">The code the mail carries.</param>
    /// <param name="lifetime">How long the code is valid, which the mail states in whole minutes, rounded up.</param>
    /// <param name="locale">The <see cref="InteractiveLocale"/> whose messages word the mail.</param>
    /// <param name="fromDisplayName">The display name of the sender, or <see langword="null"/> for the transport's
    /// own.</param>
    /// <returns>The <see cref="MailMessage"/> to send.</returns>
    public static MailMessage Compose(
        string purpose,
        string to,
        string code,
        TimeSpan lifetime,
        InteractiveLocale locale,
        string? fromDisplayName
    )
    {
        var messages = locale.Messages;
        var (subject, bodyFormat) = purpose switch
        {
            EmailCodePurposes.VerifyEmail => (
                messages.VerificationMailSubject,
                messages.VerificationMailBodyFormat
            ),
            EmailCodePurposes.ResetPassword => (
                messages.PasswordResetMailSubject,
                messages.PasswordResetMailBodyFormat
            ),
            EmailCodePurposes.SignIn => (messages.SignInMailSubject, messages.SignInMailBodyFormat),
            _ => throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "The purpose is not one of the email code purposes."
            ),
        };

        var minutes = (int)Math.Ceiling(lifetime.TotalMinutes);
        var culture = GetCulture(locale.LanguageTag);
        var body = string.Format(culture, bodyFormat, code, minutes);

        return new MailMessage
        {
            To = to,
            FromDisplayName = fromDisplayName,
            Subject = subject,
            TextBody = body,
        };
    }

    // A host may register a locale whose tag the platform has no culture data for, which words the mail all the same.
    private static CultureInfo GetCulture(string languageTag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageTag);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
