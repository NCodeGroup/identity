// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Accounts.Management.Transfers;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.Registration;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Provides extension methods to register the local-account management capability: the tenant-scoped management
/// endpoints and the validator that back the configured <c>ILocalAccountStore</c>. This opt-in package plugs into the
/// base <c>NCode.Identity.OpenId.Management</c> slice; the base slice has no knowledge of local accounts.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The <see cref="OpenIdManagementLibrary"/> builder this capability extends.</param>
    extension(IServiceBuilder<OpenIdManagementLibrary> builder)
    {
        /// <summary>
        /// Registers the local-account management endpoints (nested under each connection of the tenant-plane group,
        /// <c>/api/tenants/{tenantId}/connections/{connectionId}/local-accounts</c>), the platform routes
        /// <c>/api/platform/local-accounts</c> (nested under the root group and mapped on the control-plane cell only)
        /// and the default <see cref="ILocalAccountValidator"/>.
        /// </summary>
        /// <param name="localAccountStore">The local-account store builder — the compile-time proof that a
        /// <c>ILocalAccountStore</c> is registered, so this capability cannot be added without one.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> for method chaining.</returns>
        public IServiceBuilder<OpenIdManagementLibrary> AddLocalAccountManagement(
            IServiceBuilder<LocalAccountLibrary> localAccountStore
        )
        {
            ArgumentNullException.ThrowIfNull(localAccountStore);

            var serviceCollection = builder.ServiceCollection;

            // The handlers' XML documentation describes their OpenAPI operations only in a project that calls
            // AddOpenApi.
            serviceCollection.AddOpenApi();

            // Attach the endpoint to the existing tenant-plane group by registering it keyed under the group's name
            // (ADR-1086); the management route tree resolves keyed providers per group when it maps routes.
            var endpointDescriptor = ServiceDescriptor.KeyedSingleton<
                IEndpointProvider,
                LocalAccountApiEndpointHandler
            >(ManagementEndpointGroups.Tenant);
            serviceCollection.TryAddEnumerable(endpointDescriptor);

            // The platform routes name no tenant, so the root group serves them; the handler is shared with the tenant
            // routes.
            serviceCollection.TryAddSingleton<LocalAccountApiEndpointHandler>();
            var platformEndpointDescriptor = ServiceDescriptor.KeyedSingleton<
                IEndpointProvider,
                PlatformLocalAccountEndpointProvider
            >(ManagementEndpointGroups.Root);
            serviceCollection.TryAddEnumerable(platformEndpointDescriptor);

            serviceCollection.TryAddSingleton<
                ILocalAccountValidator,
                DefaultLocalAccountValidator
            >();
            serviceCollection.TryAddSingleton<StoredTenantSettingsReader>();
            serviceCollection.TryAddSingleton<LocalAccountPasswords>();
            serviceCollection.TryAddSingleton<ControlPlaneDefaultConnectionReader>();
            serviceCollection.TryAddSingleton<ILocalAccountCreator, DefaultLocalAccountCreator>();

            // A local connection that holds accounts cannot be deleted (ADR-1535).
            var dependentSourceDescriptor = ServiceDescriptor.Singleton<
                IConnectionDependentSource,
                LocalAccountConnectionDependentSource
            >();
            serviceCollection.TryAddEnumerable(dependentSourceDescriptor);

            // Carries a tenant's local accounts between cells (ADR-1514).
            var transferDescriptor = ServiceDescriptor.Singleton<
                ITenantTransferContributor,
                LocalAccountTenantTransferContributor
            >();
            serviceCollection.TryAddEnumerable(transferDescriptor);

            // Names the principals of local accounts in the control-plane tenant's role lists (ADR-1465).
            var describerDescriptor = ServiceDescriptor.Singleton<
                IPrincipalDescriber,
                LocalAccountPrincipalDescriber
            >();
            serviceCollection.TryAddEnumerable(describerDescriptor);

            return builder;
        }
    }
}
