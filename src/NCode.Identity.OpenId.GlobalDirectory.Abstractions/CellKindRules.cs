// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the rule for the form of a cell's kind: <see cref="Description"/>. The vocabulary of kinds is open, see
/// <see cref="CellKinds"/>, so the rule bounds only the form of a value, never its meaning.
/// </summary>
[PublicAPI]
public static class CellKindRules
{
    /// <summary>
    /// The greatest number of characters that a cell's kind holds.
    /// </summary>
    public const int MaxKindLength = 32;

    /// <summary>
    /// Gets the rule for a cell's kind in words, which every validation message that refuses one states.
    /// </summary>
    public static string Description { get; } =
        $"1 to {MaxKindLength} characters, not all white space";

    /// <summary>
    /// Determines whether the specified value is a valid cell kind.
    /// </summary>
    /// <param name="kind">The value to check.</param>
    /// <returns><see langword="true"/> when the value is a valid kind; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidKind([NotNullWhen(true)] string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && kind.Length <= MaxKindLength;
}
