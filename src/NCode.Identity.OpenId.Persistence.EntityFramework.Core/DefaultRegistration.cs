// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Registration;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the core OpenID persistence slice (its
/// entities and stores) against the Entity Framework persistence framework.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add services to.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the reusable Entity Framework persistence framework and the core OpenID schema slice: the core
        /// model contributor (which adds the core entities to the shared <see cref="OpenIdDbContext"/>), the core
        /// stores and the schema-versioned serializer of the payloads they store as JSON. Make sure to also register
        /// the Entity Framework <see cref="DbContext"/> itself.
        /// </summary>
        /// <typeparam name="TDbContext">The type of the <see cref="DbContext"/> to use.</typeparam>
        /// <returns>The <see cref="IServiceBuilder{T}"/> proving the store-manager framework is registered, which the
        /// local-account store slices require.</returns>
        [PublicAPI]
        public IServiceBuilder<StoreManagerLibrary> AddEntityFrameworkCorePersistence<TDbContext>()
            where TDbContext : DbContext
        {
            var newBuilder = serviceCollection.AddEntityFrameworkPersistenceServices<TDbContext>();

            var modelContributor = ServiceDescriptor.Singleton<
                IOpenIdModelContributor,
                CoreModelContributor
            >();
            serviceCollection.TryAddEnumerable(modelContributor);

            // The team store and the local team directory write and read the root keys of each team row through the
            // one serializer of the payloads stored at rest. TryAdd keeps a serializer registered first.
            serviceCollection.AddPersistedPayloadSerializer();

            // The audit record store stamps the time it stores each batch. TryAdd keeps a clock registered first.
            serviceCollection.TryAddSingleton(TimeProvider.System);

            serviceCollection.AddStore<TDbContext, IPlatformStore, PlatformStore>();
            serviceCollection.AddStore<TDbContext, IInstanceSetupStore, InstanceSetupStore>();
            serviceCollection.AddStore<
                TDbContext,
                IDirectoryFeedCheckpointStore,
                DirectoryFeedCheckpointStore
            >();
            serviceCollection.AddStore<
                TDbContext,
                IReplicatedDocumentStore,
                ReplicatedDocumentStore
            >();
            serviceCollection.AddStore<TDbContext, IDirectoryOutboxStore, DirectoryOutboxStore>();
            serviceCollection.AddStore<TDbContext, ITenantStore, TenantStore>();
            serviceCollection.AddStore<TDbContext, ITeamStore, TeamStore>();
            serviceCollection.AddStore<TDbContext, IClientStore, ClientStore>();
            serviceCollection.AddStore<TDbContext, IGrantStore, GrantStore>();
            serviceCollection.AddStore<TDbContext, IConsentStore, ConsentStore>();
            serviceCollection.AddStore<TDbContext, IConnectionStore, ConnectionStore>();
            serviceCollection.AddStore<TDbContext, IResourceServerStore, ResourceServerStore>();
            serviceCollection.AddStore<TDbContext, IClientGrantStore, ClientGrantStore>();
            serviceCollection.AddStore<TDbContext, IRoleAssignmentStore, RoleAssignmentStore>();
            serviceCollection.AddStore<TDbContext, ITenantUsageStore, TenantUsageStore>();
            serviceCollection.AddStore<TDbContext, IAuditRecordStore, AuditRecordStore>();
            serviceCollection.AddStore<TDbContext, IAuditStreamStore, AuditStreamStore>();
            serviceCollection.AddStore<TDbContext, IMasterKeyStore, MasterKeyStore>();
            serviceCollection.AddStore<TDbContext, IOwnerRootKeyStore, OwnerRootKeyStore>();
            serviceCollection.AddStore<TDbContext, ISecretEnvelopeStore, SecretEnvelopeStore>();
            serviceCollection.AddStore<
                TDbContext,
                IRemoteKeyReferenceStore,
                RemoteKeyReferenceStore
            >();
            serviceCollection.AddStore<
                TDbContext,
                IFederatedPrincipalStore,
                FederatedPrincipalStore
            >();
            serviceCollection.AddStore<
                TDbContext,
                IFederatedIdentityStore,
                FederatedIdentityStore
            >();

            // The local directory adapters answer from the cell's own tables. Each call opens its own context through
            // the context factory and commits its own writes, so the adapters are singletons. TryAdd lets a remote
            // adapter registered first win.
            serviceCollection.TryAddSingleton<ITenantDirectory, LocalTenantDirectory>();
            serviceCollection.TryAddSingleton<ITeamDirectory, LocalTeamDirectory>();
            serviceCollection.TryAddSingleton<IPlatformDirectory, LocalPlatformDirectory>();
            serviceCollection.TryAddSingleton<ICellDirectory, LocalCellDirectory>();

            return newBuilder;
        }
    }
}
