// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
// The project's global using of Moq also names a Match type.
using RegexMatch = System.Text.RegularExpressions.Match;

namespace NCode.Identity.OpenId.Setup;

public sealed class SetupPageTests
{
    #region Render Tests

    [Fact]
    public void Render_WhenThePageHasPlaceholders_ReplacesEveryPlaceholder()
    {
        const string html =
            "<meta content=\"__NCODE_CSP_NONCE__\"><script nonce=\"__NCODE_CSP_NONCE__\">";

        var rendered = new SetupPage(html).Render("abc", PathString.Empty);

        Assert.Equal("<meta content=\"abc\"><script nonce=\"abc\">", rendered);
    }

    [Fact]
    public void Render_WhenThePageHasNoPlaceholder_ReturnsThePageUnchanged()
    {
        var rendered = new SetupPage("<html></html>").Render("abc", PathString.Empty);

        Assert.Equal("<html></html>", rendered);
    }

    [Fact]
    public void Render_WhenThePageHasAHead_InsertsTheBaseAfterTheOpeningTag()
    {
        const string html = "<html><head><title>t</title></head><body></body></html>";

        var rendered = new SetupPage(html).Render("abc", PathString.Empty);

        Assert.Equal(
            "<html><head><base href=\"/setup/\" /><title>t</title></head><body></body></html>",
            rendered
        );
    }

    [Fact]
    public void Render_WhenTheHeadHasAttributes_InsertsTheBaseAfterTheOpeningTag()
    {
        const string html = "<html><HEAD lang=\"en\"><title>t</title></HEAD></html>";

        var rendered = new SetupPage(html).Render("abc", PathString.Empty);

        Assert.Equal(
            "<html><HEAD lang=\"en\"><base href=\"/setup/\" /><title>t</title></HEAD></html>",
            rendered
        );
    }

    [Fact]
    public void Render_WhenAPathBaseIsGiven_PrefixesTheBaseWithIt()
    {
        const string html = "<html><head></head></html>";

        var rendered = new SetupPage(html).Render("abc", new PathString("/op"));

        Assert.Contains("<base href=\"/op/setup/\" />", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenThePathBaseHoldsMarkup_EncodesItForTheAttribute()
    {
        const string html = "<html><head></head></html>";

        var rendered = new SetupPage(html).Render("abc", new PathString("/a\"><script>"));

        Assert.DoesNotContain("<script>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\"><", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenThePageHasAHeaderElementButNoHead_KeepsThePageUnchanged()
    {
        const string html = "<header></header><body></body>";

        var rendered = new SetupPage(html).Render("abc", PathString.Empty);

        Assert.Equal(html, rendered);
    }

    [Fact]
    public void Render_WhenThePageHasNoHead_KeepsThePageWithoutABase()
    {
        const string html = "<body><script nonce=\"__NCODE_CSP_NONCE__\"></script></body>";

        var rendered = new SetupPage(html).Render("abc", PathString.Empty);

        Assert.Equal("<body><script nonce=\"abc\"></script></body>", rendered);
    }

    [BuiltSetupBundleFact]
    public async Task Render_WhenGivenTheBuiltBundlesIndex_LeavesNoPlaceholderAndNoncesEveryScript()
    {
        var indexPath = BuiltSetupBundleFactAttribute.IndexPath ?? string.Empty;

        var html = await File.ReadAllTextAsync(
            indexPath,
            SecureEncoding.UTF8,
            CancellationToken.None
        );

        var rendered = new SetupPage(html).Render("n0nce", PathString.Empty);

        Assert.DoesNotContain(SetupPage.NoncePlaceholder, rendered, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/setup/\" />", rendered, StringComparison.Ordinal);

        var scriptTags = Regex.Matches(rendered, "<script\\b[^>]*>");
        Assert.NotEmpty(scriptTags);
        Assert.All(
            scriptTags,
            tag => Assert.Contains("nonce=\"n0nce\"", tag.Value, StringComparison.Ordinal)
        );
    }

    [BuiltSetupBundleFact]
    public void Render_WhenGivenTheBuiltBundlesIndex_NoncesEveryStylesheetAndServesEveryTypefaceItLoads()
    {
        var indexPath = BuiltSetupBundleFactAttribute.IndexPath ?? string.Empty;

        const string assetsPath = "/setup/assets/";
        var html = File.ReadAllText(indexPath, SecureEncoding.UTF8);
        var bundleRootPath = Path.GetDirectoryName(indexPath);
        using var bundle = new SinglePageBundle<SetupPage>(
            bundleRootPath,
            static html => new SetupPage(html)
        );
        var httpContext = new DefaultHttpContext();

        var links = Regex.Matches(html, "<link rel=\"stylesheet\"[^>]*>");
        Assert.NotEmpty(links);
        var fonts = new HashSet<Uri>();
        foreach (var link in links.Cast<RegexMatch>())
        {
            // Vite's html.cspNonce marks the stylesheet as it marks the scripts, so style-src admits it.
            Assert.Contains(
                $"nonce=\"{SetupPage.NoncePlaceholder}\"",
                link.Value,
                StringComparison.Ordinal
            );

            var asset = Regex
                .Match(link.Value, "href=\"\\./assets/(?<asset>[^\"/]+\\.css)\"")
                .Groups["asset"]
                .Value;
            Assert.NotEmpty(asset);

            var css = File.ReadAllText(
                Path.Combine(bundleRootPath ?? string.Empty, "assets", asset),
                SecureEncoding.UTF8
            );
            var stylesheetUri = new Uri($"https://op.example{assetsPath}{asset}");
            foreach (
                var match in Regex
                    .Matches(css, "url\\((?<quote>[\"']?)(?<url>[^\"')]+)\\k<quote>\\)")
                    .Cast<RegexMatch>()
            )
            {
                fonts.Add(new Uri(stylesheetUri, match.Groups["url"].Value));
            }
        }

        Assert.Equal(4, fonts.Count);
        Assert.All(
            fonts,
            font =>
            {
                Assert.Equal("https://op.example", font.GetLeftPart(UriPartial.Authority));
                Assert.StartsWith(assetsPath, font.AbsolutePath, StringComparison.Ordinal);
                var result = bundle.ServeAsset(
                    httpContext.Response,
                    font.AbsolutePath[assetsPath.Length..]
                );
                var file = Assert.IsType<PhysicalFileHttpResult>(result);
                Assert.Equal("font/woff2", file.ContentType);
            }
        );
    }

    [BuiltSetupBundleTheory]
    [InlineData("OFL-Instrument-Sans", "Copyright 2022 The Instrument Sans Project Authors")]
    [InlineData("OFL-Martian-Mono", "Copyright 2020 The Martian Mono Project Authors")]
    public void Render_WhenGivenTheBuiltBundlesIndex_ShipsEachTypefacesLicenseBesideTheFonts(
        string licenseName,
        string copyright
    )
    {
        var indexPath = BuiltSetupBundleFactAttribute.IndexPath ?? string.Empty;

        var bundleRootPath = Path.GetDirectoryName(indexPath);
        using var bundle = new SinglePageBundle<SetupPage>(
            bundleRootPath,
            static html => new SetupPage(html)
        );
        var httpContext = new DefaultHttpContext();

        var license = Assert.Single(
            Directory.GetFiles(
                Path.Combine(bundleRootPath ?? string.Empty, "assets"),
                $"{licenseName}-*.txt"
            )
        );
        var result = bundle.ServeAsset(httpContext.Response, Path.GetFileName(license));

        var file = Assert.IsType<PhysicalFileHttpResult>(result);
        Assert.Equal("text/plain", file.ContentType);
        var text = File.ReadAllText(license, SecureEncoding.UTF8);
        Assert.StartsWith(copyright, text, StringComparison.Ordinal);
    }

    #endregion
}
