// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.Persistence;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Provides extension methods for <see cref="IServiceBuilder{TMarker}"/> to register named local-account stores.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The builder that proves a store-manager persistence framework is registered.</param>
    extension(IServiceBuilder<StoreManagerLibrary> builder)
    {
        /// <summary>
        /// Registers a named local-account store. Every store registration calls it: it adds the registration to the
        /// <see cref="LocalAccountStoreCatalog"/>, registers the <c>local</c> connection strategy once and registers
        /// the <see cref="LocalAccountLibrary"/> marker once, however many stores are registered.
        /// </summary>
        /// <param name="factory">Creates the registration of the store.</param>
        /// <returns>The builder of the <see cref="LocalAccountLibrary"/> marker.</returns>
        [PublicAPI]
        public IServiceBuilder<LocalAccountLibrary> AddLocalAccountStore(
            Func<IServiceProvider, LocalAccountStoreRegistration> factory
        )
        {
            ArgumentNullException.ThrowIfNull(factory);

            var serviceCollection = builder.ServiceCollection;
            serviceCollection.AddSingleton(factory);
            serviceCollection.TryAddSingleton<LocalAccountStoreCatalog>();

            var descriptor = ServiceDescriptor.Singleton<
                IConnectionStrategy,
                LocalConnectionStrategy
            >();
            serviceCollection.TryAddEnumerable(descriptor);

            var warmup = ServiceDescriptor.Singleton<
                IHostedService,
                LocalAccountStoreCatalogWarmup
            >();
            serviceCollection.TryAddEnumerable(warmup);

            return serviceCollection.GetOrAddBuilder<LocalAccountLibrary>();
        }
    }
}
