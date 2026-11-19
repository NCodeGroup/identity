// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Frontend.TestSupport;

/// <summary>
/// A typeface's license that every built bundle ships beside the fonts.
/// </summary>
/// <param name="Name">The license's file name before the build's content hash, such as <c>OFL-Martian-Mono</c>.</param>
/// <param name="Copyright">The copyright line that the license's text starts with.</param>
public sealed record TypefaceLicense(string Name, string Copyright)
{
    /// <summary>
    /// Finds the license among a built bundle's assets, where the build writes it under its name and a content hash,
    /// and fails unless exactly one asset matches.
    /// </summary>
    /// <param name="assetsFolderPath">The directory that holds the bundle's assets.</param>
    /// <returns>The path of the license's asset.</returns>
    public string FindAsset(string assetsFolderPath)
    {
        var searchPattern = $"{Name}-*.txt";
        var assets = Directory.GetFiles(assetsFolderPath, searchPattern);
        return Assert.Single(assets);
    }
}
