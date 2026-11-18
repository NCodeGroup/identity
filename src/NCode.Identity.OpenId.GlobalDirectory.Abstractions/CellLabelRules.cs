// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the rule for the form of a cell's name and its region: <see cref="Description"/>. A cell's machine client
/// identifier is <see cref="GlobalDirectoryProtocol.CellClientIdPrefix"/> followed by its name. Every valid cell name
/// is also a valid tag of the keys a cell creates in a key management service.
/// </summary>
[PublicAPI]
public static partial class CellLabelRules
{
    /// <summary>
    /// The greatest number of characters that a cell's name or region holds.
    /// </summary>
    public const int MaxLabelLength = 31;

    /// <summary>
    /// Gets the rule for a cell's name and region in words, which every validation message that refuses one states.
    /// </summary>
    public static string Description { get; } =
        $"1 to {MaxLabelLength} characters of lowercase letters, digits and single hyphens, starting with a letter and ending with a letter or digit, with no two hyphens in a row";

    // A lowercase letter, then letters and digits that each take at most one hyphen before them, so no hyphen ends the
    // label or follows another. '\z' rather than '$', which also matches before a trailing newline. MaxLabelLength
    // bounds the length outside the pattern.
    [GeneratedRegex(@"^[a-z](?:-?[a-z0-9])*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    /// <summary>
    /// Determines whether the specified value is a valid cell name or region.
    /// </summary>
    /// <param name="label">The value to check.</param>
    /// <returns><see langword="true"/> when the value is a valid label; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidLabel([NotNullWhen(true)] string? label) =>
        !string.IsNullOrEmpty(label)
        && label.Length <= MaxLabelLength
        && LabelRegex().IsMatch(label);
}
