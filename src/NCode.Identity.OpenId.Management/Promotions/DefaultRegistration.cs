// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides extension methods to configure the services that promote one tenant's configuration into another.
/// </summary>
// See ADR-1425.
internal static class DefaultRegistration
{
    extension(IEndpointGroupBuilder builder)
    {
        /// <summary>
        /// Registers the promotion services, the tenant configuration exporter and the built-in contributors of the
        /// tenant's settings, connections, resource servers, clients and client grants.
        /// </summary>
        public void AddTenantPromotionServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton<
                IPromotedSettingsComposer,
                DefaultPromotedSettingsComposer
            >();
            serviceCollection.TryAddSingleton<ITenantPromoter, DefaultTenantPromoter>();
            serviceCollection.TryAddSingleton<
                ITenantConfigurationExporter,
                DefaultTenantConfigurationExporter
            >();
            serviceCollection.TryAddSingleton<
                IHostManagedClientChecker,
                DefaultHostManagedClientChecker
            >();
            serviceCollection.TryAddSingleton<
                IClientKeySourceChecker,
                DefaultClientKeySourceChecker
            >();
            serviceCollection.TryAddSingleton<IClientJwksUriChecker, DefaultClientJwksUriChecker>();

            var tenantSettingsDescriptor = ServiceDescriptor.Singleton<
                ITenantPromotionContributor,
                TenantSettingsPromotionContributor
            >();
            serviceCollection.TryAddEnumerable(tenantSettingsDescriptor);

            var connectionDescriptor = ServiceDescriptor.Singleton<
                ITenantPromotionContributor,
                ConnectionPromotionContributor
            >();
            serviceCollection.TryAddEnumerable(connectionDescriptor);

            var resourceServerDescriptor = ServiceDescriptor.Singleton<
                ITenantPromotionContributor,
                ResourceServerPromotionContributor
            >();
            serviceCollection.TryAddEnumerable(resourceServerDescriptor);

            var clientDescriptor = ServiceDescriptor.Singleton<
                ITenantPromotionContributor,
                ClientPromotionContributor
            >();
            serviceCollection.TryAddEnumerable(clientDescriptor);

            var clientGrantDescriptor = ServiceDescriptor.Singleton<
                ITenantPromotionContributor,
                ClientGrantPromotionContributor
            >();
            serviceCollection.TryAddEnumerable(clientGrantDescriptor);
        }
    }
}
