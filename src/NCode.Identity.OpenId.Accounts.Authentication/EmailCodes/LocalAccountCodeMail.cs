// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// The model of the mail that carries a one-time code, which <see cref="LocalAccountMailComposer"/> composes into a
/// message at each delivery attempt. The code is a number, so it is never text until the composer writes it into a
/// sensitive buffer.
/// </summary>
/// <param name="To">The address the mail is sent to.</param>
/// <param name="FromDisplayName">The display name of the sender, or <see langword="null"/> for the transport's
/// own.</param>
/// <param name="Subject">The subject of the mail, in the wording of the locale.</param>
/// <param name="BodyFormat">The composite format of the body, in the wording of the locale. Its first item is the code
/// and its second the lifetime in minutes.</param>
/// <param name="LanguageTag">The language tag of the locale, which selects the culture that formats the body.</param>
/// <param name="Code">The code the mail carries, which the body shows as six digits.</param>
/// <param name="LifetimeMinutes">How long the code is valid, in whole minutes, rounded up.</param>
// See ADR-1533.
internal sealed record LocalAccountCodeMail(
    string To,
    string? FromDisplayName,
    string Subject,
    string BodyFormat,
    string LanguageTag,
    int Code,
    int LifetimeMinutes
)
{
    /// <summary>
    /// Writes the members the record prints, which leave out the code and the address, so neither reaches a log, a
    /// debugger display or an exception message.
    /// </summary>
    /// <param name="builder">The builder that receives the printed members.</param>
    /// <returns><see langword="true"/>, because members were written.</returns>
    // See ADR-1533.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(
            CultureInfo.InvariantCulture,
            $"LanguageTag = {LanguageTag}, LifetimeMinutes = {LifetimeMinutes}"
        );
        return true;
    }
}
