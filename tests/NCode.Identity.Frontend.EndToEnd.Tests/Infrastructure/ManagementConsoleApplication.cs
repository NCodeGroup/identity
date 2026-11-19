// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCode.Identity.EndToEnd.TestSupport;
using NCode.Identity.ManagementConsole.Host;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Hosts the management console's reference host on Kestrel over HTTPS with the built apps/mgmt bundle, so that real
/// browsers can open its page; it reaches the OpenID Provider only when a user signs in.
/// </summary>
public sealed class ManagementConsoleApplication
    : WebApplicationFactory<ManagementConsoleHostMarker>
{
    private const string ClientKeyRing = "mgmt-client";
    private const string DPoPKeyRing = "mgmt-dpop";

    /// <summary>
    /// Builds and starts the management console's reference host.
    /// </summary>
    /// <param name="certificate">The certificate that the host presents.</param>
    /// <param name="port">The loopback port that the host listens on.</param>
    /// <param name="authority">The origin of the OpenID Provider that the console signs users in with.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public static async ValueTask<ManagementConsoleApplication> StartAsync(
        TestCertificate certificate,
        int port,
        string authority,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrEmpty(authority);
        cancellationToken.ThrowIfCancellationRequested();

        var application = new ManagementConsoleApplication($"https://localhost:{port}", authority);
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

    private List<ECDsa> Keys { get; } = [];
    private string Authority { get; }

    private ManagementConsoleApplication(string origin, string authority)
    {
        Origin = origin;
        Authority = authority;
    }

    /// <summary>
    /// Gets the origin of the host, which is <c>https://localhost:{port}</c>.
    /// </summary>
    public string Origin { get; }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            foreach (var key in Keys)
            {
                key.Dispose();
            }
        }
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Resolve the content root to the host project so its appsettings.json is loaded.
        builder.UseSolutionRelativeContentRoot("hosts/NCode.Identity.ManagementConsole.Host");
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureLogging(logging => logging.ClearProviders());

        // The host reads these while it registers its services, so each is a host setting.
        var bundleRootPath = FrontendBundles.GetRootPath(FrontendBundles.ManagementConsoleAppName);
        builder.UseSetting("LocalDevelopment:Enabled", "true");
        builder.UseSetting("Bff:Frontends:mgmt:Authority", Authority);
        builder.UseSetting("Bff:Frontends:mgmt:PublicOrigin", Origin);
        builder.UseSetting("ManagementConsole:ManagementApiUrl", $"{Authority}/api");
        builder.UseSetting("ManagementConsole:BundleRootPath", bundleRootPath);

        builder.ConfigureTestServices(serviceCollection =>
        {
            serviceCollection.AddDataProtection().UseEphemeralDataProtectionProvider();
            AddKeyRing(serviceCollection, ClientKeyRing);
            AddKeyRing(serviceCollection, DPoPKeyRing);
        });
    }

    private void AddKeyRing(IServiceCollection serviceCollection, string name)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Keys.Add(key);

        serviceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
            name,
            (serviceProvider, _) =>
            {
                var keyFactory = serviceProvider.GetRequiredService<ISecretKeyFactory>();
                var metadata = new KeyMetadata { KeyId = name };
                var secretKey = keyFactory.CreateEcc(metadata, key);
                var providerFactory =
                    serviceProvider.GetRequiredService<ISecretKeyCollectionProviderFactory>();
                return providerFactory.CreateStatic([secretKey]);
            }
        );
    }
}
