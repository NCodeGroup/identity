// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using NCode.Identity.EndToEnd.TestSupport;
using Xunit;

namespace NCode.Identity.Bff.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The environment that the end-to-end tests share: Keycloak in a container, three backend-for-frontend (BFF)
/// applications, the origin pages and the browsers.
/// </summary>
public sealed class EndToEndEnvironment : IAsyncLifetime
{
    private const string StrictClientId = "bff-strict";
    private const string LaxClientId = "bff-lax";
    private const string BlazorClientId = "bff-blazor";
    private const string ContainerHost = BffHostSetup.ContainerHost;

    private static InvalidOperationException NotInitialized() =>
        new("The end-to-end environment has not been initialized.");

    private TestCertificate? Certificate { get; set; }
    private KeycloakProvider? KeycloakOrNull { get; set; }
    private BffApplication? StrictBffOrNull { get; set; }
    private BffApplication? LaxBffOrNull { get; set; }
    private BlazorBffApplication? BlazorBffOrNull { get; set; }
    private OriginServer? OriginServer { get; set; }
    private BrowserPool? BrowserPoolOrNull { get; set; }
    private string? CrossSiteDeniedOrNull { get; set; }
    private string? CrossSiteAdmittedOrNull { get; set; }
    private string? SameSiteUntrustedOrNull { get; set; }
    private string? SameSiteTrustedOrNull { get; set; }

    /// <summary>
    /// Gets Keycloak, the OpenID Provider.
    /// </summary>
    public KeycloakProvider Keycloak => KeycloakOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets the BFF whose session cookie is <c>SameSite=Strict</c>.
    /// </summary>
    public BffApplication StrictBff => StrictBffOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets the BFF whose session cookie is <c>SameSite=Lax</c>.
    /// </summary>
    public BffApplication LaxBff => LaxBffOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets the BFF that hosts the Blazor Web App, whose session cookie is <c>SameSite=Strict</c>.
    /// </summary>
    public BlazorBffApplication BlazorBff => BlazorBffOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets a cross-site origin that the CORS policy does not admit.
    /// </summary>
    public string CrossSiteDenied => CrossSiteDeniedOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets a cross-site origin that the CORS policy admits.
    /// </summary>
    public string CrossSiteAdmitted => CrossSiteAdmittedOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets a same-site origin that the CORS policy admits and that is not trusted.
    /// </summary>
    public string SameSiteUntrusted => SameSiteUntrustedOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets a same-site origin that the CORS policy admits and that is trusted.
    /// </summary>
    public string SameSiteTrusted => SameSiteTrustedOrNull ?? throw NotInitialized();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            var strictPort = LoopbackPort.Reserve();
            var laxPort = LoopbackPort.Reserve();
            var blazorPort = LoopbackPort.Reserve();
            var crossSiteDeniedPort = LoopbackPort.Reserve();
            var crossSiteAdmittedPort = LoopbackPort.Reserve();
            var sameSiteUntrustedPort = LoopbackPort.Reserve();
            var sameSiteTrustedPort = LoopbackPort.Reserve();

            CrossSiteDeniedOrNull = $"https://127.0.0.1:{crossSiteDeniedPort}";
            CrossSiteAdmittedOrNull = $"https://127.0.0.1:{crossSiteAdmittedPort}";
            SameSiteUntrustedOrNull = $"https://localhost:{sameSiteUntrustedPort}";
            SameSiteTrustedOrNull = $"https://localhost:{sameSiteTrustedPort}";

            Certificate = TestCertificate.Create();

            await TestcontainersSettings.ExposeHostPortsAsync([
                (ushort)strictPort,
                (ushort)laxPort,
                (ushort)blazorPort,
            ]);

            KeycloakOrNull = await KeycloakProvider.StartAsync(
                Certificate,
                new Dictionary<string, string>
                {
                    ["BFF_STRICT_ORIGIN"] = $"https://localhost:{strictPort}",
                    ["BFF_STRICT_CONTAINER_ORIGIN"] = $"https://{ContainerHost}:{strictPort}",
                    ["BFF_LAX_ORIGIN"] = $"https://localhost:{laxPort}",
                    ["BFF_LAX_CONTAINER_ORIGIN"] = $"https://{ContainerHost}:{laxPort}",
                    ["BFF_BLAZOR_ORIGIN"] = $"https://localhost:{blazorPort}",
                    ["BFF_BLAZOR_CONTAINER_ORIGIN"] = $"https://{ContainerHost}:{blazorPort}",
                },
                CancellationToken.None
            );

            StrictBffOrNull = await BffApplication.StartAsync(
                CreateSettings(StrictClientId, strictPort, SameSiteMode.Strict),
                CancellationToken.None
            );
            LaxBffOrNull = await BffApplication.StartAsync(
                CreateSettings(LaxClientId, laxPort, SameSiteMode.Lax),
                CancellationToken.None
            );
            BlazorBffOrNull = await BlazorBffApplication.StartAsync(
                new BffApplicationSettings(
                    BlazorClientId,
                    blazorPort,
                    SameSiteMode.Strict,
                    Keycloak.Issuer,
                    Certificate,
                    [],
                    [],
                    TimeSpan.FromSeconds(2)
                ),
                CancellationToken.None
            );

            OriginServer = await OriginServer.StartAsync(
                Certificate,
                [
                    crossSiteDeniedPort,
                    crossSiteAdmittedPort,
                    sameSiteUntrustedPort,
                    sameSiteTrustedPort,
                ],
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
                var originServer = OriginServer;
                OriginServer = null;
                if (originServer is not null)
                {
                    await originServer.DisposeAsync();
                }
            },
            async () =>
            {
                var blazorBff = BlazorBffOrNull;
                BlazorBffOrNull = null;
                if (blazorBff is not null)
                {
                    await blazorBff.DisposeAsync();
                }
            },
            async () =>
            {
                var laxBff = LaxBffOrNull;
                LaxBffOrNull = null;
                if (laxBff is not null)
                {
                    await laxBff.DisposeAsync();
                }
            },
            async () =>
            {
                var strictBff = StrictBffOrNull;
                StrictBffOrNull = null;
                if (strictBff is not null)
                {
                    await strictBff.DisposeAsync();
                }
            },
            async () =>
            {
                var keycloak = KeycloakOrNull;
                KeycloakOrNull = null;
                if (keycloak is not null)
                {
                    await keycloak.DisposeAsync();
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

    private BffApplicationSettings CreateSettings(
        string clientId,
        int port,
        SameSiteMode sameSite
    ) =>
        new(
            clientId,
            port,
            sameSite,
            Keycloak.Issuer,
            Certificate ?? throw NotInitialized(),
            [SameSiteUntrusted, SameSiteTrusted, CrossSiteAdmitted],
            [SameSiteTrusted],
            TimeSpan.FromMinutes(1)
        );
}
