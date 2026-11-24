// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Accounts;
using NCode.Identity.OpenId.Accounts.Credentials;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;

/// <summary>
/// Provides extension methods to register the generic Entity Framework local-account reference implementation.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <summary>
    /// Creates the registration of the store under the specified name.
    /// </summary>
    /// <param name="serviceProvider">The service provider that creates the store.</param>
    /// <param name="storeName">The name of the store.</param>
    /// <returns>The registration whose factories create the store.</returns>
    private static LocalAccountStoreRegistration CreateRegistration(
        IServiceProvider serviceProvider,
        string storeName
    ) =>
        new()
        {
            Name = storeName,
            CreateStore = storeManager => CreateStore(serviceProvider, storeManager),
            CreateTransferStore = storeManager => CreateStore(serviceProvider, storeManager),
        };

    /// <summary>
    /// Creates the store over the specified store manager.
    /// </summary>
    /// <param name="serviceProvider">The service provider that supplies the store's dependencies.</param>
    /// <param name="storeManager">The store manager the store reads and writes through.</param>
    /// <returns>The new store.</returns>
    private static LocalAccountStore CreateStore(
        IServiceProvider serviceProvider,
        IStoreManager storeManager
    )
    {
        var service = storeManager.GetService(typeof(OpenIdDbContext));
        var dbContext =
            service as OpenIdDbContext
            ?? throw new InvalidOperationException(
                "The store manager does not provide an OpenIdDbContext, which the local-account store requires."
            );
        return ActivatorUtilities.CreateInstance<LocalAccountStore>(
            serviceProvider,
            storeManager,
            dbContext
        );
    }

    /// <param name="builder">The builder proving the store-manager framework
    /// (<c>AddEntityFrameworkCorePersistence</c>) is registered.</param>
    extension(IServiceBuilder<StoreManagerLibrary> builder)
    {
        /// <summary>
        /// Registers the generic Entity Framework <see cref="ILocalAccountStore"/> and
        /// <see cref="ILocalAccountTransferStore"/> under a name that a local connection's <c>store</c> option selects,
        /// together with the <see cref="LocalAccountLibrary"/> marker that the opt-in local-account capabilities
        /// require as a compile-time argument, and the model contributor that adds the account entities to the shared
        /// <c>OpenIdDbContext</c>. The store hashes and verifies passwords with the <see cref="IPasswordHasher"/> that
        /// the host registers, for example with <c>AddDefaultPasswordHasher</c> from the
        /// <c>NCode.Identity.OpenId.Accounts</c> package. The receiver proves the Entity Framework persistence
        /// framework is registered.
        /// </summary>
        /// <param name="storeName">The name of the store, <see cref="LocalAccountStoreNames.Default"/> when
        /// omitted.</param>
        /// <returns>The <see cref="IServiceBuilder{T}"/> proving a local-account store is registered.</returns>
        public IServiceBuilder<LocalAccountLibrary> AddLocalAccountEntityFramework(
            string storeName = LocalAccountStoreNames.Default
        )
        {
            ArgumentException.ThrowIfNullOrEmpty(storeName);

            var serviceCollection = builder.ServiceCollection;

            var modelContributor = ServiceDescriptor.Singleton<
                IOpenIdModelContributor,
                LocalAccountModelContributor
            >();
            serviceCollection.TryAddEnumerable(modelContributor);

            return builder.AddLocalAccountStore(serviceProvider =>
                CreateRegistration(serviceProvider, storeName)
            );
        }
    }
}
