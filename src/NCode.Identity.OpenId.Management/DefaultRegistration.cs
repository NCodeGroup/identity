// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Management.Endpoints.Secrets;
using NCode.Identity.OpenId.Management.OpenApi;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Management.ResourceServers;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Registration;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management;

/// <summary>
/// Provides extension methods to register the OpenID management library and its services.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    // Picks the adapter of a control-plane port: the local adapter on the control-plane cell, otherwise the remote
    // adapter registered under ControlPlaneServiceKeys.Remote, otherwise the local adapter.
    private static TService ResolveControlPlaneAdapter<TService, TLocal>(
        IServiceProvider serviceProvider
    )
        where TService : class
        where TLocal : class, TService
    {
        var cell = serviceProvider.GetRequiredService<IOptions<CellOptions>>().Value;
        if (cell.IsControlPlane)
            return serviceProvider.GetRequiredService<TLocal>();

        return serviceProvider.GetKeyedService<TService>(ControlPlaneServiceKeys.Remote)
            ?? serviceProvider.GetRequiredService<TLocal>();
    }

    /// <param name="builder">The <see cref="OpenIdCoreLibrary"/> builder this library extends.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Registers the OpenID management library, its authorization handlers, and API endpoints. The
        /// <see cref="IManagementTokenValidator"/> and <see cref="IControlPlaneAuthority"/> a host resolves are the
        /// local adapters on the control-plane cell (<see cref="CellOptions.IsControlPlane"/>), and otherwise the
        /// remote adapters registered under <see cref="ControlPlaneServiceKeys.Remote"/>, falling back to the local
        /// adapters when none is registered.
        /// </summary>
        /// <returns>An <see cref="IServiceBuilder{TLibrary}"/> for the registered <see cref="OpenIdManagementLibrary"/>.</returns>
        [PublicAPI]
        public IServiceBuilder<OpenIdManagementLibrary> AddOpenIdManagementLibrary()
        {
            var serviceCollection = builder.ServiceCollection;

            // OwnershipHandler is an ASP.NET Core authorization handler invoked without the request's OpenIdContext in
            // hand, so it reads the ambient context from the request (the one blessed async-local usage).
            serviceCollection.AddHttpContextAccessor();

            serviceCollection.AddAuthorization();
            serviceCollection.AddAuthorizationHandler<GlobalAdminHandler>();
            serviceCollection.AddAuthorizationHandler<OwnershipHandler>();

            // Enrich the OpenAPI document with descriptions for the management API's shared parameters.
            serviceCollection.AddOpenApi(openApiOptions =>
                openApiOptions.AddDocumentTransformer<ManagementParameterDocumentTransformer>()
            );

            // Answers every bodyless error under the management prefix as a problem, in every host (ADR-1483).
            var startupFilterDescriptor = ServiceDescriptor.Singleton<
                IStartupFilter,
                ManagementProblemResponseStartupFilter
            >();
            serviceCollection.TryAddEnumerable(startupFilterDescriptor);

            serviceCollection.TryAddSingleton<
                IResourceOwnershipService,
                DefaultResourceOwnershipService
            >();

            serviceCollection.TryAddSingleton<
                IAuthorityChainResolver,
                DefaultAuthorityChainResolver
            >();

            // Validates the management access token of a request. The control-plane cell uses the local adapter; every
            // other cell uses the remote adapter that a directory client registers under
            // ControlPlaneServiceKeys.Remote, and falls back to the local adapter without one.
            serviceCollection.TryAddSingleton<LocalManagementTokenValidator>();
            serviceCollection.TryAddSingleton<IManagementTokenValidator>(serviceProvider =>
                ResolveControlPlaneAdapter<
                    IManagementTokenValidator,
                    LocalManagementTokenValidator
                >(serviceProvider)
            );

            // Answers the platform and team roles of a caller, from the local adapter or the remote adapter on the
            // same terms as the token validator.
            serviceCollection.TryAddSingleton<LocalControlPlaneAuthority>();
            serviceCollection.TryAddSingleton<IControlPlaneAuthority>(serviceProvider =>
                ResolveControlPlaneAdapter<IControlPlaneAuthority, LocalControlPlaneAuthority>(
                    serviceProvider
                )
            );

            // Describes the control-plane principals a query names, on the same terms.
            serviceCollection.TryAddSingleton<LocalControlPlanePrincipalDirectory>();
            serviceCollection.TryAddSingleton<IControlPlanePrincipalDirectory>(serviceProvider =>
                ResolveControlPlaneAdapter<
                    IControlPlanePrincipalDirectory,
                    LocalControlPlanePrincipalDirectory
                >(serviceProvider)
            );
            var principalDescriberDescriptor = ServiceDescriptor.Singleton<
                IPrincipalDescriber,
                ClientPrincipalDescriber
            >();
            serviceCollection.TryAddEnumerable(principalDescriberDescriptor);

            serviceCollection.TryAddSingleton<
                IManagementTenantEnvironmentResolver,
                DefaultManagementTenantEnvironmentResolver
            >();

            serviceCollection.TryAddSingleton<
                IManagementAuditRecorder,
                DefaultManagementAuditRecorder
            >();

            // Shared by the tenant/client secret-create endpoints (ADR-1424).
            serviceCollection.TryAddSingleton<ISecretValidator, DefaultSecretValidator>();

            // Shared by the tenant/client public-key read endpoints (ADR-1268). Delegates to each key's
            // ISecretKeyTypeDescriptor (the single extensibility seam, ADR-1104) for the JWK/PEM representation.
            serviceCollection.TryAddSingleton<
                ISecretPublicKeyConverter,
                DefaultSecretPublicKeyConverter
            >();

            // Binds a key a key management service already holds to a tenant, after proving the key signs (ADR-1476).
            serviceCollection.TryAddSingleton<IRemoteSecretImporter, DefaultRemoteSecretImporter>();

            // The transfer keys are protected under the host's Data Protection key ring (ADR-1514).
            serviceCollection.AddDataProtection();

            // Issues the one-time keys that seal a tenant's export to this cell (ADR-1514).
            serviceCollection.TryAddSingleton<
                ITenantTransferKeyService,
                DefaultTenantTransferKeyService
            >();

            // Carries a tenant's core records between cells (ADR-1514).
            var transferContributorDescriptor = ServiceDescriptor.Singleton<
                ITenantTransferContributor,
                CoreTenantTransferContributor
            >();
            serviceCollection.TryAddEnumerable(transferContributorDescriptor);

            var managementSeedDescriptor = ServiceDescriptor.Singleton<
                ICommandHandler<SeedTenantCommand>,
                ManagementResourceServerSeedHandler
            >();
            serviceCollection.TryAddEnumerable(managementSeedDescriptor);

            var controlPlaneSeedDescriptor = ServiceDescriptor.Singleton<
                ICommandHandler<SeedTenantCommand>,
                ControlPlaneResourceServerSeedHandler
            >();
            serviceCollection.TryAddEnumerable(controlPlaneSeedDescriptor);

            // The root group declares its own endpoints and child groups (ADR-1086); registering it wires the whole
            // /api tree.
            builder.AddEndpointGroup<ManagementRootGroup>();

            return builder.NewBuilder<OpenIdManagementLibrary>();
        }
    }
}
