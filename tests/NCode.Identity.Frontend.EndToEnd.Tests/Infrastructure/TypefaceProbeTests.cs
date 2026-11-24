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
        let lagging = 1;
        FontFaceSet.prototype.check = function (...args) {
          return lagging-- > 0 ? false : check.apply(this, args);
        };
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

        Assert.Equal(1, result.InstrumentSansFaces);
        Assert.Equal(1, result.MartianMonoFaces);
        Assert.True(result.BothChecked);
    }

    #endregion
}
