// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using Xunit;

namespace NCode.Identity.Frontend.TestSupport;

/// <summary>
/// Asserts that a built single-page bundle loads its typefaces under its page's policy and ships their licenses, as its
/// host serves them.
/// </summary>
/// <typeparam name="TIndex">The type of the bundle's parsed <c>index.html</c>.</typeparam>
/// <param name="layout">The bundle and the address its host serves the bundle's assets at.</param>
/// <param name="parseIndex">Parses the bundle's <c>index.html</c>.</param>
public sealed class BuiltBundleTypefaces<TIndex>(
    BuiltBundleLayout layout,
    Func<string, TIndex?> parseIndex
) : IDisposable
    where TIndex : class
{
    private const string StylesheetLinkPattern = "<link rel=\"stylesheet\"[^>]*>";
    private const string FontUrlPattern = "url\\((?<quote>[\"']?)(?<url>[^\"')]+)\\k<quote>\\)";
    private const int FontCount = 4;

    private BuiltBundleLayout Layout { get; } = layout;

    private SinglePageBundle<TIndex> Bundle { get; } = new(layout.RootPath, parseIndex);

    /// <summary>
    /// Asserts that every stylesheet link of the bundle's <c>index.html</c> carries the nonce placeholder, and that the
    /// host serves each of the four fonts that the stylesheets load from the bundle's assets as <c>font/woff2</c>.
    /// </summary>
    public void AssertNoncesEveryStylesheetAndServesEveryTypeface()
    {
        var html = File.ReadAllText(Layout.IndexPath, SecureEncoding.UTF8);
        IReadOnlyList<Match> links = Regex.Matches(html, StylesheetLinkPattern);
        Assert.NotEmpty(links);

        var fonts = new HashSet<Uri>();
        foreach (var link in links)
        {
            AddFonts(link.Value, fonts);
        }

        Assert.Equal(FontCount, fonts.Count);
        Assert.All(fonts, AssertServedAsFont);
    }

    /// <summary>
    /// Asserts that the bundle ships one copy of a typeface's license among its assets, that the host serves it as
    /// <c>text/plain</c>, and that its text starts with the license's copyright line.
    /// </summary>
    /// <param name="licenseName">The license's file name before the build's content hash.</param>
    /// <param name="copyright">The copyright line that the license's text starts with.</param>
    public void AssertShipsLicenseBesideTheFonts(string licenseName, string copyright)
    {
        var license = new TypefaceLicense(licenseName, copyright);
        var licensePath = license.FindAsset(Layout.AssetsFolderPath);
        var assetPath = Path.GetFileName(licensePath);
        var httpContext = new DefaultHttpContext();

        var result = Bundle.ServeAsset(httpContext.Response, assetPath);

        var file = Assert.IsType<PhysicalFileHttpResult>(result);
        Assert.Equal("text/plain", file.ContentType);
        var text = File.ReadAllText(licensePath, SecureEncoding.UTF8);
        Assert.StartsWith(license.Copyright, text, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose() => Bundle.Dispose();

    private void AddFonts(string link, HashSet<Uri> fonts)
    {
        // Vite's html.cspNonce marks the stylesheet as it marks the scripts, so style-src admits it.
        var nonce = $"nonce=\"{Layout.NoncePlaceholder}\"";
        Assert.Contains(nonce, link, StringComparison.Ordinal);

        var hrefPrefix = Regex.Escape(Layout.StylesheetHrefPrefix);
        var hrefPattern = $"href=\"{hrefPrefix}(?<asset>[^\"/]+\\.css)\"";
        var href = Regex.Match(link, hrefPattern);
        var asset = href.Groups["asset"].Value;
        Assert.NotEmpty(asset);

        var stylesheetPath = Path.Combine(Layout.AssetsFolderPath, asset);
        var css = File.ReadAllText(stylesheetPath, SecureEncoding.UTF8);
        var stylesheetUri = new Uri(Layout.AssetsUri, asset);
        IReadOnlyList<Match> urls = Regex.Matches(css, FontUrlPattern);
        foreach (var url in urls)
        {
            var relativeUrl = url.Groups["url"].Value;
            var font = new Uri(stylesheetUri, relativeUrl);
            fonts.Add(font);
        }
    }

    private void AssertServedAsFont(Uri font)
    {
        var assetsAuthority = Layout.AssetsUri.GetLeftPart(UriPartial.Authority);
        var fontAuthority = font.GetLeftPart(UriPartial.Authority);
        Assert.Equal(assetsAuthority, fontAuthority);

        var assetsPath = Layout.AssetsUri.AbsolutePath;
        Assert.StartsWith(assetsPath, font.AbsolutePath, StringComparison.Ordinal);

        var assetPath = font.AbsolutePath[assetsPath.Length..];
        var httpContext = new DefaultHttpContext();
        var result = Bundle.ServeAsset(httpContext.Response, assetPath);

        var file = Assert.IsType<PhysicalFileHttpResult>(result);
        Assert.Equal("font/woff2", file.ContentType);
    }
}
