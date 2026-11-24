// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the rule for the form of a connection name: 1 to 128 ASCII letters, digits, hyphens or underscores that
/// start with a letter or digit.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static partial class ConnectionNameRules
{
    /// <summary>
    /// The most characters a connection name holds.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Gets the pattern that a valid connection name matches.
    /// </summary>
    /// <returns>The generated regular expression.</returns>
    /// <remarks>
    /// '\z' rather than '$', which also matches before a trailing newline. {0,127} is MaxLength minus the first
    /// character, which the pattern matches apart.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    /// <summary>
    /// Determines whether the specified value is a valid connection name.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> when the value is a valid name; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid([NotNullWhen(true)] string? name) =>
        !string.IsNullOrEmpty(name) && NameRegex().IsMatch(name);
}
