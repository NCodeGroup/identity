// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCode.Identity.EndToEnd.TestSupport;
using NCode.Identity.OpenId.Host;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Hosts the OpenID Provider's reference host on Kestrel over HTTPS with the built apps/auth and apps/setup bundles, in
/// its local-development posture with no database, so that real browsers can open its pages.
/// </summary>
/// <remarks>
/// Without a connection the host keeps its OpenID data in one in-memory database per process, so a run starts one host.
/// </remarks>
public sealed class OpenIdHostApplication : WebApplicationFactory<HostApiMarker>
{
    /// <summary>
    /// Builds and starts the OpenID Provider's reference host.
    /// </summary>
    /// <param name="certificate">The certificate that the host presents.</param>
    /// <param name="port">The loopback port that the host listens on.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public static async ValueTask<OpenIdHostApplication> StartAsync(
        TestCertificate certificate,
        int port,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(certificate);
        cancellationToken.ThrowIfCancellationRequested();

        var application = new OpenIdHostApplication($"https://localhost:{port}");
        try
        {
            application.UseKestrel(kestrel =>
                kestrel.ListenLocalhost(port, listen => listen.UseHttps(certificate.Certificate))
            );
            application.StartServer();
            return application;
        }
        catch (Exception startFailure)
        {
            await Teardown.StopStartedAsync(startFailure, application.DisposeAsync);
            throw;
        }
    }

    private OpenIdHostApplication(string origin)
    {
        Origin = origin;
    }

    /// <summary>
    /// Gets the origin of the host, which is <c>https://localhost:{port}</c>.
    /// </summary>
    public string Origin { get; }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Resolve the content root to the host project so its appsettings.json is loaded.
        builder.UseSolutionRelativeContentRoot("hosts/NCode.Identity.OpenId.Host");
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureLogging(logging => logging.ClearProviders());

        // The host reads these while it registers its services, so each is a host setting.
        var authBundleRootPath = FrontendBundles.GetRootPath("auth");
        var setupBundleRootPath = FrontendBundles.GetRootPath("setup");
        builder.UseSetting("LocalDevelopment:Enabled", "true");
        builder.UseSetting("OpenId:TenantResolution:StaticSingle:Issuer", Origin);
        builder.UseSetting("InteractiveAuthentication:BundleRootPath", authBundleRootPath);
        builder.UseSetting("Setup:BundleRootPath", setupBundleRootPath);

        builder.ConfigureTestServices(serviceCollection =>
            serviceCollection.AddDataProtection().UseEphemeralDataProtectionProvider()
        );
    }
}
