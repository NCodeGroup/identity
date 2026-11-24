// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.EndToEnd.TestSupport;
using Xunit;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

[Collection(FrontendCollection.Name)]
public sealed class TypefaceProbeTests(FrontendEnvironment environment)
{
    private const string LoginPagePath = "/account/login?ReturnUrl=%2F";

    private const string LaggingCheckScript = """
        const check = FontFaceSet.prototype.check;
        window.__fontChecks = 0;
        FontFaceSet.prototype.check = function (...args) {
          return window.__fontChecks++ === 0 ? false : check.apply(this, args);
        };
        """;

    private const string FontChecksScript = """
        () => window.__fontChecks
        """;

    private const string NeverSettlingFontSetScript = """
        FontFaceSet.prototype.load = () => new Promise(() => {});
        FontFaceSet.prototype.check = () => false;
        """;

    #region OpenAsync Tests

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task OpenAsync_WhenCheckLagsBehindLoad_ReportsBothCheckedOnceSettled(
        string browserName
    )
    {
        var origin = environment.OpenIdHost.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        // The font set reports its first check unloaded, as WebKit can right after a load resolves.
        await context.AddInitScriptAsync(LaggingCheckScript);

        var result = await TypefaceProbe.OpenAsync(
            context,
            $"{origin}{LoginPagePath}",
            CancellationToken.None
        );
        var page = Assert.Single(context.Pages);
        var fontChecks = await page.EvaluateAsync<int>(FontChecksScript);

        Assert.Equal(1, result.InstrumentSansFaces);
        Assert.Equal(1, result.MartianMonoFaces);
        Assert.True(result.BothChecked);
        Assert.Empty(result.FontFailures);

        // The probe found a family unchecked on its first read of both, so it read the font set again.
        Assert.True(fontChecks > 2);
    }

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task OpenAsync_WhenTypefacesAreBlocked_ReportsNeitherLoadedNorChecked(
        string browserName
    )
    {
        var origin = environment.OpenIdHost.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        await context.RouteAsync("**/*.woff2", route => route.AbortAsync());

        var result = await TypefaceProbe.OpenAsync(
            context,
            $"{origin}{LoginPagePath}",
            CancellationToken.None
        );

        Assert.Equal(0, result.InstrumentSansFaces);
        Assert.Equal(0, result.MartianMonoFaces);
        Assert.False(result.BothChecked);
        Assert.Contains(
            result.FontFailures,
            failure => failure.StartsWith(TypefaceProbe.InstrumentSans, StringComparison.Ordinal)
        );
        Assert.Contains(
            result.FontFailures,
            failure => failure.StartsWith(TypefaceProbe.MartianMono, StringComparison.Ordinal)
        );
    }

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task OpenAsync_WhenFontSetNeverSettles_ReportsNeitherLoadedNorChecked(
        string browserName
    )
    {
        var origin = environment.OpenIdHost.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        await context.AddInitScriptAsync(NeverSettlingFontSetScript);

        var result = await TypefaceProbe.OpenAsync(
            context,
            $"{origin}{LoginPagePath}",
            CancellationToken.None
        );

        Assert.Equal(0, result.InstrumentSansFaces);
        Assert.Equal(0, result.MartianMonoFaces);
        Assert.False(result.BothChecked);
        Assert.Contains(
            result.FontFailures,
            failure => failure.StartsWith(TypefaceProbe.InstrumentSans, StringComparison.Ordinal)
        );
        Assert.Contains(
            result.FontFailures,
            failure => failure.StartsWith(TypefaceProbe.MartianMono, StringComparison.Ordinal)
        );
    }

    #endregion
}
