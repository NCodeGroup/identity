// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.EndToEnd.TestSupport;
using NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;
using Xunit;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Pages;

[Collection(FrontendCollection.Name)]
public sealed class PageTypefaceTests(FrontendEnvironment environment)
{
    #region Login Page Tests

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task LoginPage_WhenOpened_LoadsBothTypefacesFromItsAssetsUnderItsPolicy(
        string browserName
    )
    {
        var origin = environment.OpenIdHost.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        var result = await TypefaceProbe.OpenAsync(
            context,
            $"{origin}/account/login?ReturnUrl=%2F",
            CancellationToken.None
        );

        AssertLoadedUnderPolicy(result, origin, "/account/assets/");
    }

    #endregion

    #region Setup Page Tests

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task SetupPage_WhenOpened_LoadsBothTypefacesFromItsAssetsUnderItsPolicy(
        string browserName
    )
    {
        var origin = environment.OpenIdHost.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        var result = await TypefaceProbe.OpenAsync(
            context,
            $"{origin}/setup/",
            CancellationToken.None
        );

        AssertLoadedUnderPolicy(result, origin, "/setup/assets/");
    }

    #endregion

    #region Management Console Page Tests

    [Theory, MemberData(nameof(BrowserNames.All), MemberType = typeof(BrowserNames))]
    public async Task ManagementConsolePage_WhenOpened_LoadsBothTypefacesFromItsAssetsUnderItsPolicy(
        string browserName
    )
    {
        var origin = environment.ManagementConsole.Origin;
        await using var context = await environment.NewContextAsync(
            browserName,
            CancellationToken.None
        );

        var result = await TypefaceProbe.OpenAsync(context, $"{origin}/", CancellationToken.None);

        AssertLoadedUnderPolicy(result, origin, "/assets/");
    }

    #endregion

    private static void AssertLoadedUnderPolicy(
        TypefaceProbeResult result,
        string origin,
        string assetsPath
    )
    {
        Assert.Empty(result.PolicyViolations);
        Assert.Empty(result.PolicyMessages);

        // Loading a family for its space character loads its latin face, the one face whose unicode-range holds it.
        Assert.Equal(1, result.InstrumentSansFaces);
        Assert.Equal(1, result.MartianMonoFaces);
        Assert.True(result.BothChecked);

        Assert.Contains(
            result.FontResponses,
            response =>
                response.Url.AbsolutePath.Contains(
                    "/instrument-sans-latin-wdth-normal-",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            result.FontResponses,
            response =>
                response.Url.AbsolutePath.Contains(
                    "/martian-mono-latin-wdth-normal-",
                    StringComparison.Ordinal
                )
        );

        Assert.All(
            result.FontResponses,
            response =>
            {
                Assert.Equal(origin, response.Url.GetLeftPart(UriPartial.Authority));
                Assert.StartsWith(assetsPath, response.Url.AbsolutePath, StringComparison.Ordinal);
                Assert.Equal(200, response.Status);
                Assert.Equal("font/woff2", response.ContentType);
            }
        );
    }
}
