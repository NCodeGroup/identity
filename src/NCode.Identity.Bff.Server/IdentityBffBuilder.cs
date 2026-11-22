// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NCode.Identity.AspNetCore;
using NCode.Identity.Events;
using NCode.Identity.Jose;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Client.KeyRings;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.SharedState;

namespace NCode.Identity.Bff.Server;

/// <summary>
/// Provides the default implementation of <see cref="IIdentityBffBuilder"/>: the composition root that references the
/// implementation packages a BFF needs and wires them together.
/// </summary>
// See ADR-1500.
internal sealed class IdentityBffBuilder : ServiceBuilder<IdentityBff>, IIdentityBffBuilder
{
    /// <inheritdoc />
    public IServiceBuilder<SharedStateLibrary> SharedStateLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<EventsLibrary> EventsLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<SecretsLibrary> SecretsLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<JoseLibrary> JoseLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<JwtLibrary> JwtLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<OpenIdClientLibrary> OpenIdClientLibraryBuilder { get; }

    /// <inheritdoc />
    public IServiceBuilder<BffLibrary> BffLibraryBuilder { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityBffBuilder"/> class.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">
    /// The configuration whose <c>Bff</c> section configures the BFF, whose <c>KeyRings</c> section configures its key
    /// rings and whose <c>LocalDevelopment</c> section turns on the relaxations a local inner-loop host needs.
    /// </param>
    public IdentityBffBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
        : base(serviceCollection)
    {
        // The in-process adapters are the defaults, correct for a single replica; a deployment that runs several swaps
        // in shared adapters (AddValkey) on SharedStateLibraryBuilder, or registers its own first.
        SharedStateLibraryBuilder = serviceCollection
            .AddSharedStateServices()
            .AddInProcessExpiringKeySet()
            .AddInProcessExpiringValueSet()
            .AddInProcessExpiringIndexSet();

        EventsLibraryBuilder = serviceCollection.AddEventServices();

        SecretsLibraryBuilder = serviceCollection.AddSecretsLibrary();
        JoseLibraryBuilder = SecretsLibraryBuilder.AddJoseLibrary();
        JwtLibraryBuilder = JoseLibraryBuilder.AddJwtLibrary();
        OpenIdClientLibraryBuilder = JwtLibraryBuilder.AddOpenIdClientLibrary();

        // Key rings are the protocol client's, so they live in the host's top-level KeyRings section (ADR-1439).
        var keyRingsSection = configuration.GetSection(OpenIdClientDefaults.KeyRingsSectionName);
        OpenIdClientLibraryBuilder.AddOpenIdClientKeyRings(keyRingsSection);

        BffLibraryBuilder = OpenIdClientLibraryBuilder
            .AddBffLibrary(EventsLibraryBuilder)
            .AddBffConfiguration(configuration);

        // No deployment serves every Host header, so the composition root, not the host, refuses an empty or wildcard
        // AllowedHosts unless the host is configured for local development (ADR-1370).
        serviceCollection.AddHostFilteringValidation(configuration);

        // Only the composition root knows both the BFF and the protocol client, so it checks the lease against the
        // client's request timeout (ADR-1291).
        var startupCheckDescriptor = ServiceDescriptor.Singleton<
            IHostedService,
            BffRefreshLeaseStartupCheck
        >();
        serviceCollection.TryAddEnumerable(startupCheckDescriptor);
    }
}
