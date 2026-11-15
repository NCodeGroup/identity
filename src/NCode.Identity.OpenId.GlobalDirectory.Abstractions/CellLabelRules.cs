// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the rule for the form of a cell's name and its region: 1 to 31 lowercase letters, digits or hyphens that
/// starts with a letter and ends with a letter or digit. A cell's machine client identifier is
/// <see cref="GlobalDirectoryProtocol.CellClientIdPrefix"/> followed by its name.
/// </summary>
[PublicAPI]
public static partial class CellLabelRules
{
    /// <summary>
    /// The greatest number of characters that a cell's name or region holds.
    /// </summary>
    public const int MaxLabelLength = 31;

    // A lowercase RFC 1123 host label that starts with a letter. '\z' rather than '$', which also matches before a
    // trailing newline. The first and last characters and the up to 29 between them spell MaxLabelLength.
    [GeneratedRegex(@"^[a-z](?:[a-z0-9-]{0,29}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    /// <summary>
    /// Determines whether the specified value is a valid cell name or region.
    /// </summary>
    /// <param name="label">The value to check.</param>
    /// <returns><see langword="true"/> when the value is a valid label; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidLabel([NotNullWhen(true)] string? label) =>
        !string.IsNullOrEmpty(label) && LabelRegex().IsMatch(label);
}
