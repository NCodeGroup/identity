// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents a custom header of a mail message.
/// </summary>
// See ADR-1532.
[PublicAPI]
public sealed class MailHeader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MailHeader"/> class.
    /// </summary>
    /// <param name="name">The name of the header, which holds printable ASCII characters and no colon.</param>
    /// <param name="value">The value of the header, which holds no carriage return, line feed or NUL.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty or holds a character outside ASCII 33 to 126 or a colon, or when
    /// <paramref name="value"/> holds a carriage return, a line feed or a NUL.
    /// </exception>
    public MailHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        if (name.AsSpan().ContainsAnyExceptInRange('\u0021', '\u007E') || name.Contains(':'))
            throw new ArgumentException(
                "The name must hold only ASCII characters from 33 to 126 and no colon.",
                nameof(name)
            );

        if (value.AsSpan().ContainsAny('\r', '\n', '\0'))
            throw new ArgumentException(
                "The value must not hold a carriage return, a line feed or a NUL.",
                nameof(value)
            );

        Name = name;
        Value = value;
    }

    /// <summary>
    /// Gets the name of the header.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the value of the header.
    /// </summary>
    public string Value { get; }
}
