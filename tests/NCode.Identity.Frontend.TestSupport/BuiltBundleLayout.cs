// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.TestSupport;

/// <summary>
/// Describes a built single-page bundle and the address its host serves the bundle's assets at.
/// </summary>
/// <param name="IndexPath">The absolute path of the bundle's built <c>index.html</c>.</param>
/// <param name="NoncePlaceholder">The placeholder that the build writes as the nonce of each script and stylesheet
/// link.</param>
/// <param name="StylesheetHrefPrefix">The text that each stylesheet link's <c>href</c> starts with in the built
/// <c>index.html</c>.</param>
/// <param name="AssetsUri">The absolute address that the host serves the bundle's assets at, ending in a slash.</param>
public sealed record BuiltBundleLayout(
    string IndexPath,
    string NoncePlaceholder,
    string StylesheetHrefPrefix,
    Uri AssetsUri
)
{
    /// <summary>
    /// Gets the directory that holds the bundle's <c>index.html</c>.
    /// </summary>
    public string RootPath => Path.GetDirectoryName(IndexPath) ?? string.Empty;

    /// <summary>
    /// Gets the directory that holds the bundle's assets.
    /// </summary>
    public string AssetsFolderPath => Path.Combine(RootPath, "assets");
}
