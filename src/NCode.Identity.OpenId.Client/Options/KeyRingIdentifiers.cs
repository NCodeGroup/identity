// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace NCode.Identity.OpenId.Client.Options;

/// <summary>
/// The rule for the name of a key ring and the id of a key in it: letters and digits in groups separated by single
/// <c>.</c>, <c>_</c> or <c>-</c>, at most 64 characters, so that it never holds the <c>__</c> configuration separator
/// and is a valid environment variable name. Two ids of one key ring are compared without case, as .NET configuration
/// keys are.
/// </summary>
// See ADR-1439, ADR-1479 and ADR-1485.
internal static partial class KeyRingIdentifiers
{
    /// <summary>
    /// The most characters that a name or an id holds.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The rule that a refused name or id is told, which completes "must be".
    /// </summary>
    public const string Description =
        "letters and digits in groups separated by single '.', '_' or '-', at most 64 characters";

    /// <summary>
    /// Gets whether a value is a valid key ring name or key id.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value follows the rule; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) =>
        value is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(value);

    /// <summary>
    /// The pattern of a valid name or id.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
