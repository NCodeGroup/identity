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
    /// Determines whether a value is one bare mail address, such as <c>noreply@example.com</c>: MimeKit parses it as a
    /// mailbox with no display name, its address is the value as written, and it holds an <c>@</c>.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns><see langword="true"/> when the value is one bare mail address; otherwise,
    /// <see langword="false"/>.</returns>
    public static bool IsMailAddress([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // A transport sends from the value as an address and takes the display name from its own FromName, so a display
        // name, angle brackets or a comment would reach the provider inside the address. See ADR-1532.
        return MailboxAddress.TryParse(value, out var address)
            && string.IsNullOrEmpty(address.Name)
            && string.Equals(address.Address, value, StringComparison.Ordinal)
            && address.Address.Contains('@', StringComparison.Ordinal);
    }
}
