// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Playwright;
using NCode.Identity.EndToEnd.TestSupport;
using Xunit;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The environment the pages suite shares: the OpenID Provider and management console hosts with their built bundles,
/// and the browsers.
/// </summary>
public sealed class FrontendEnvironment : IAsyncLifetime
{
    private TestCertificate? Certificate { get; set; }
    private OpenIdHostApplication? OpenIdHostOrNull { get; set; }
    private ManagementConsoleApplication? ManagementConsoleOrNull { get; set; }
    private BrowserPool? BrowserPoolOrNull { get; set; }

    /// <summary>
    /// Gets the OpenID Provider's reference host.
    /// </summary>
    public OpenIdHostApplication OpenIdHost => OpenIdHostOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets the management console's reference host.
    /// </summary>
    public ManagementConsoleApplication ManagementConsole =>
        ManagementConsoleOrNull ?? throw NotInitialized();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Certificate = TestCertificate.Create();

        OpenIdHostOrNull = await OpenIdHostApplication.StartAsync(
            Certificate,
            LoopbackPort.Reserve(),
            CancellationToken.None
        );
        ManagementConsoleOrNull = await ManagementConsoleApplication.StartAsync(
            Certificate,
            LoopbackPort.Reserve(),
            OpenIdHost.Origin,
            CancellationToken.None
        );

        BrowserPoolOrNull = await BrowserPool.CreateAsync(CancellationToken.None);
    }

    /// <summary>
    /// Creates a browser context that ignores certificate errors, in a browser that is launched once per run.
    /// </summary>
    public async ValueTask<IBrowserContext> NewContextAsync(
        string browserName,
        CancellationToken cancellationToken
    )
    {
        return await (BrowserPoolOrNull ?? throw NotInitialized()).NewContextAsync(
            browserName,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (BrowserPoolOrNull is not null)
            await BrowserPoolOrNull.DisposeAsync();

        if (ManagementConsoleOrNull is not null)
            await ManagementConsoleOrNull.DisposeAsync();

        if (OpenIdHostOrNull is not null)
            await OpenIdHostOrNull.DisposeAsync();

        Certificate?.Dispose();
    }

    private static InvalidOperationException NotInitialized() =>
        new("The frontend end-to-end environment has not been initialized.");
}
