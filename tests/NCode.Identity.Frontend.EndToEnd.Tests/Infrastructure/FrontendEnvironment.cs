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
    private static InvalidOperationException NotInitialized() =>
        new("The frontend end-to-end environment has not been initialized.");

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
        try
        {
            Certificate = TestCertificate.Create();
            var openIdHostPort = LoopbackPort.Reserve();
            var managementConsolePort = LoopbackPort.Reserve();

            OpenIdHostOrNull = await OpenIdHostApplication.StartAsync(
                Certificate,
                openIdHostPort,
                CancellationToken.None
            );
            ManagementConsoleOrNull = await ManagementConsoleApplication.StartAsync(
                Certificate,
                managementConsolePort,
                OpenIdHost.Origin,
                CancellationToken.None
            );

            BrowserPoolOrNull = await BrowserPool.CreateAsync(CancellationToken.None);
        }
        // A failed start stops what it already started, then fails the run.
        catch (Exception startFailure)
        {
            await Teardown.StopStartedAsync(startFailure, async () => await DisposeAsync());
            throw;
        }
    }

    /// <summary>
    /// Creates a browser context that ignores certificate errors, in a browser that is launched once per run.
    /// </summary>
    public async ValueTask<IBrowserContext> NewContextAsync(
        string browserName,
        CancellationToken cancellationToken
    )
    {
        var pool = BrowserPoolOrNull ?? throw NotInitialized();
        return await pool.NewContextAsync(browserName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // Every stop runs even when an earlier one throws, and each clears its property first, so a second call stops
        // nothing twice.
        await Teardown.StopAllAsync(
            async () =>
            {
                var pool = BrowserPoolOrNull;
                BrowserPoolOrNull = null;
                if (pool is not null)
                {
                    await pool.DisposeAsync();
                }
            },
            async () =>
            {
                var managementConsole = ManagementConsoleOrNull;
                ManagementConsoleOrNull = null;
                if (managementConsole is not null)
                {
                    await managementConsole.DisposeAsync();
                }
            },
            async () =>
            {
                var openIdHost = OpenIdHostOrNull;
                OpenIdHostOrNull = null;
                if (openIdHost is not null)
                {
                    await openIdHost.DisposeAsync();
                }
            },
            () =>
            {
                var certificate = Certificate;
                Certificate = null;
                certificate?.Dispose();
                return ValueTask.CompletedTask;
            }
        );
    }
}
