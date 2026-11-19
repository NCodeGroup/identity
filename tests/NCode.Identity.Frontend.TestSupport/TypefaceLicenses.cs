// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Frontend.TestSupport;

/// <summary>
/// The licenses of the two typefaces that every built bundle ships beside the fonts.
/// </summary>
public static class TypefaceLicenses
{
    /// <summary>
    /// Gets the license of each typeface.
    /// </summary>
    public static IReadOnlyList<TypefaceLicense> All { get; } =
    [
        new("OFL-Instrument-Sans", "Copyright 2022 The Instrument Sans Project Authors"),
        new("OFL-Martian-Mono", "Copyright 2020 The Martian Mono Project Authors"),
    ];

    /// <summary>
    /// Gets the name and copyright line of each license, as theory data.
    /// </summary>
    public static TheoryData<string, string> Theories { get; } = CreateTheories();

    private static TheoryData<string, string> CreateTheories()
    {
        var theories = new TheoryData<string, string>();
        foreach (var license in All)
        {
            theories.Add(license.Name, license.Copyright);
        }

        return theories;
    }
}
