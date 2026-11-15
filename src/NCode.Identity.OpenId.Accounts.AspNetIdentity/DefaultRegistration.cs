// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Provides extension methods to register the ASP.NET Core Identity <see cref="ILocalAccountStore"/> adapter.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    // Replaces the last registration of the host's hasher in place with one that wraps it, so the order of this call
    // and of ASP.NET Core Identity's registration, which keeps a registration that exists, does not matter. See
    // ADR-1238.
    private static void AddNormalizingPasswordHasher<TUser>(IServiceCollection serviceCollection)
        where TUser : class
    {
        for (var index = serviceCollection.Count - 1; index >= 0; index--)
        {
            var descriptor = serviceCollection[index];
            if (
                descriptor.ServiceType != typeof(IPasswordHasher<TUser>)
                || descriptor.IsKeyedService
            )
            {
                continue;
            }

            serviceCollection[index] = ServiceDescriptor.Describe(
                typeof(IPasswordHasher<TUser>),
                serviceProvider =>
                {
                    var innerHasher = CreateInnerHasher<TUser>(serviceProvider, descriptor);
                    return Wrap(innerHasher);
                },
                descriptor.Lifetime
            );
            return;
        }

        serviceCollection.AddScoped<IPasswordHasher<TUser>>(serviceProvider =>
        {
            var innerHasher = ActivatorUtilities.CreateInstance<PasswordHasher<TUser>>(
                serviceProvider
            );
            return Wrap(innerHasher);
        });
    }

    private static IPasswordHasher<TUser> CreateInnerHasher<TUser>(
        IServiceProvider serviceProvider,
        ServiceDescriptor descriptor
    )
        where TUser : class
    {
        if (descriptor.ImplementationInstance is { } implementationInstance)
        {
            return (IPasswordHasher<TUser>)implementationInstance;
        }

        if (descriptor.ImplementationFactory is { } implementationFactory)
        {
            var created = implementationFactory(serviceProvider);
            return (IPasswordHasher<TUser>)created;
        }

        if (descriptor.ImplementationType is { } implementationType)
        {
            var instance = ActivatorUtilities.CreateInstance(serviceProvider, implementationType);
            return (IPasswordHasher<TUser>)instance;
        }

        throw new InvalidOperationException(
            $"The registration of IPasswordHasher<{typeof(TUser).Name}> has no instance, factory or implementation type."
        );
    }

    private static NormalizingPasswordHasher<TUser> Wrap<TUser>(IPasswordHasher<TUser> innerHasher)
        where TUser : class =>
        innerHasher as NormalizingPasswordHasher<TUser>
        ?? new NormalizingPasswordHasher<TUser>(innerHasher);

    /// <param name="builder">The builder proving the store-manager framework (the Entity Framework persistence) is
    /// registered — the adapter registers a factory the store manager resolves.</param>
    extension(IServiceBuilder<StoreManagerLibrary> builder)
    {
        /// <summary>
        /// Registers an <see cref="ILocalAccountStore"/> and an <see cref="ILocalAccountTransferStore"/> that adapt the
        /// ASP.NET Core Identity <c>UserManager&lt;TUser&gt;</c> for the specified user type, together with the
        /// <see cref="LocalAccountLibrary"/> marker that the opt-in local-account capabilities require as a
        /// compile-time argument. The host is responsible for registering ASP.NET Core Identity
        /// (<c>AddIdentityCore&lt;TUser&gt;</c> or <c>AddIdentity</c>) and its stores. It wraps the host's
        /// <c>IPasswordHasher&lt;TUser&gt;</c> registration, made before or after this call, in
        /// <see cref="NormalizingPasswordHasher{TUser}"/>, so the adapter and the host's own ASP.NET Core Identity
        /// pages hash and verify a password's NFC form; a hasher that the host registers after this call must be
        /// wrapped by the host. It also registers an <see cref="ILookupNormalizer"/> that prefixes each normalized
        /// user name and email with the ambient tenant's normalized identifier, so a user name and an email are
        /// unique per tenant; the user type must implement <see cref="ITenantBoundUser"/>. The host's Identity schema
        /// must size <c>NormalizedUserName</c> to at least <see cref="AccountMaxLengths.NormalizedUserName"/>
        /// characters, <c>NormalizedEmail</c> to at least <see cref="AccountMaxLengths.NormalizedEmail"/>, and
        /// <c>UserName</c> and <c>Email</c> to at least <see cref="AccountMaxLengths.UserName"/> and
        /// <see cref="AccountMaxLengths.Email"/>; the default Entity Framework Core model of ASP.NET Core Identity
        /// sizes all four at 256, which is shorter.
        /// </summary>
        /// <typeparam name="TUser">The ASP.NET Core Identity user type.</typeparam>
        /// <returns>The <see cref="IServiceBuilder{T}"/> proving a local-account store is registered.</returns>
        public IServiceBuilder<LocalAccountLibrary> AddAspNetIdentityLocalAccountStore<TUser>()
            where TUser : IdentityUser, ITenantBoundUser, new()
        {
            var serviceCollection = builder.ServiceCollection;

            var newBuilder = serviceCollection.NewBuilder<LocalAccountLibrary>();

            // ASP.NET Core Identity's AddIdentityCore keeps a registration that exists, so this one wins whether it is
            // made before or after the host's.
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<ILookupNormalizer, TenantLookupNormalizer>()
            );

            // See ADR-1239.
            serviceCollection.TryAddSingleton<AspNetIdentityDummyPasswordHash<TUser>>();

            // See ADR-1238.
            AddNormalizingPasswordHasher<TUser>(serviceCollection);

            // The store is backing-store-agnostic, so it registers the DbContext-less factory the store manager falls
            // back to; the scoped UserManager is resolved per operation from a fresh scope.
            serviceCollection.AddSingleton<Func<IStoreProvider, ILocalAccountStore>>(
                serviceProvider =>
                    storeProvider =>
                        ActivatorUtilities.CreateInstance<AspNetIdentityLocalAccountStore<TUser>>(
                            serviceProvider,
                            storeProvider
                        )
            );

            serviceCollection.AddSingleton<Func<IStoreProvider, ILocalAccountTransferStore>>(
                serviceProvider =>
                    storeProvider =>
                        ActivatorUtilities.CreateInstance<AspNetIdentityLocalAccountStore<TUser>>(
                            serviceProvider,
                            storeProvider
                        )
            );

            return newBuilder;
        }
    }
}
