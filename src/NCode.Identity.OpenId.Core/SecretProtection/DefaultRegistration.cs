// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Dispatch;
using NCode.Identity.OpenId.Exceptions;
using NCode.Identity.Registration;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.SecretProtection;

/// <summary>
/// Provides extension methods to configure the services that find the values master keys protect.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Registers the sources of the protected values the family stores (stored secrets and grant payloads), the
        /// announcement of master key and owner root key changes, the job that rewraps the values of retired keys, the
        /// policy that keeps the control-plane tenant under the deployment's root key, the resolver that names a tenant's
        /// team chain as the ancestors whose root keys can wrap its master keys, the services that maintain
        /// master keys and validate the root keys that owners bring, and the job and the service that sweep the
        /// remote keys that no stored secret names. The team-chain resolver is the registered
        /// <see cref="ISecretOwnerParentResolver"/>, in place of the secret persistence library's default, so a host
        /// registers its own resolver after this call, with <c>AddSingleton</c> or <c>Replace</c>.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddSecretProtectionServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IProtectedValueSource, PersistedSecretValueSource>()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IProtectedValueSource, GrantPayloadValueSource>()
            );

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IMasterKeyChangeObserver, MasterKeyChangeAnnouncer>()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IReplicaBroadcastHandler,
                    MasterKeyChangeBroadcastHandler
                >()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Scoped<
                    IDispatchJobHandler<MasterKeyRewrapJob>,
                    MasterKeyRewrapJobHandler
                >()
            );

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IOwnerRootKeyChangeObserver,
                    OwnerRootKeyChangeAnnouncer
                >()
            );

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IOwnerRootKeyPolicy, ControlPlaneTenantRootKeyPolicy>()
            );

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IOpenIdExceptionTranslator,
                    OwnerRootKeyUnavailableExceptionTranslator
                >()
            );

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Scoped<
                    IDispatchJobHandler<RemoteKeySweepJob>,
                    RemoteKeySweepJobHandler
                >()
            );

            // The secret persistence library registers its default resolver with TryAdd, so Replace makes the team
            // chain win whichever registration runs first; TryAdd here would keep the library default, which
            // AddIdentityServer registers first. A host registers its own resolver after AddIdentityServer().
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<
                    ISecretOwnerParentResolver,
                    TeamChainSecretOwnerParentResolver
                >()
            );

            serviceCollection.AddHostedService<MasterKeyMaintenanceService>();
            serviceCollection.AddHostedService<OwnerRootKeyValidationService>();
            serviceCollection.AddHostedService<RemoteKeySweepService>();

            return builder;
        }
    }
}
