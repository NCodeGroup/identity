// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using MimeKit;

namespace NCode.Identity.Mail.Mime;

/// <summary>
/// Provides the form rule of the mail address that a transport sending MIME is configured to send from.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class MimeMailAddresses
{
    /// <summary>
    /// Determines whether a value is one mail address that MimeKit parses and that holds an <c>@</c>.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns><see langword="true"/> when the value is one mail address; otherwise,
    /// <see langword="false"/>.</returns>
    public static bool IsMailAddress([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return MailboxAddress.TryParse(value, out var address)
            && address.Address.Contains('@', StringComparison.Ordinal);
    }
}
