// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents a mailbox: an address and an optional display name.
/// </summary>
// See ADR-1532.
[PublicAPI]
public sealed class MailAddress
{
    /// <summary>
    /// Checks whether a value holds a control character: a character from U+0000 to U+001F, or U+007F.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value holds a control character; otherwise
    /// <see langword="false"/>.</returns>
    private static bool HasControlCharacter(string value) =>
        value.AsSpan().ContainsAnyInRange('\u0000', '\u001F') || value.Contains('\u007F');

    /// <summary>
    /// Initializes a new instance of the <see cref="MailAddress"/> class.
    /// </summary>
    /// <param name="address">The address of the mailbox, such as <c>user@example.com</c>.</param>
    /// <param name="displayName">The display name of the mailbox, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="address"/> is empty or has no <c>@</c> between its first and last characters, or
    /// when either value holds a control character.
    /// </exception>
    public MailAddress(string address, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);

        var at = address.IndexOf('@', 1);
        if (at < 0 || at == address.Length - 1)
            throw new ArgumentException(
                "The address must hold an '@' between its first and last characters.",
                nameof(address)
            );

        if (HasControlCharacter(address))
            throw new ArgumentException(
                "The address must not hold a control character.",
                nameof(address)
            );

        if (displayName is not null && HasControlCharacter(displayName))
            throw new ArgumentException(
                "The display name must not hold a control character.",
                nameof(displayName)
            );

        Address = address;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the address of the mailbox.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the display name of the mailbox, or <see langword="null"/> for none.
    /// </summary>
    public string? DisplayName { get; }
}
