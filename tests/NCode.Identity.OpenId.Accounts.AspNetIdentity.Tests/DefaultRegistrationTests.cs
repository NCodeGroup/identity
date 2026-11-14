// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

public sealed class DefaultRegistrationTests : IDisposable
{
    private const string ComposedPassword = "caf\u00E9";
    private const string DecomposedPassword = "cafe\u0301";
    private const string MadeHash = "hash";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region Helpers

    private static IPasswordHasher<TestTenantUser> ResolveHasher(
        IServiceCollection serviceCollection
    )
    {
        using var provider = serviceCollection.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPasswordHasher<TestTenantUser>>();
    }

    #endregion

    #region AddAspNetIdentityLocalAccountStore Tests

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenIdentityIsAddedAfterIt_ResolvesTheNormalizingHasher()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();
        serviceCollection.AddIdentityCore<TestTenantUser>();

        var hasher = ResolveHasher(serviceCollection);

        Assert.IsType<NormalizingPasswordHasher<TestTenantUser>>(hasher);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenIdentityIsAddedBeforeIt_ResolvesTheNormalizingHasher()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddIdentityCore<TestTenantUser>();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        var hasher = ResolveHasher(serviceCollection);

        Assert.IsType<NormalizingPasswordHasher<TestTenantUser>>(hasher);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenTheHostRegisteredItsOwnHasher_WrapsIt()
    {
        var innerHasher = Mocks.Create<IPasswordHasher<TestTenantUser>>();
        innerHasher
            .Setup(x => x.HashPassword(It.IsAny<TestTenantUser>(), ComposedPassword))
            .Returns(MadeHash)
            .Verifiable();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(innerHasher.Object);
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        var hasher = ResolveHasher(serviceCollection);

        var hash = hasher.HashPassword(new TestTenantUser("alice"), DecomposedPassword);
        Assert.Equal(MadeHash, hash);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenTheHostRegisteredANormalizingHasher_DoesNotWrapItAgain()
    {
        var innerHasher = Mocks.Create<IPasswordHasher<TestTenantUser>>();
        var registered = new NormalizingPasswordHasher<TestTenantUser>(innerHasher.Object);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IPasswordHasher<TestTenantUser>>(registered);
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        var hasher = ResolveHasher(serviceCollection);

        Assert.Same(registered, hasher);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenTheHostRegisteredASingletonHasherType_KeepsItsLifetime()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<
            IPasswordHasher<TestTenantUser>,
            PasswordHasher<TestTenantUser>
        >();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPasswordHasher<TestTenantUser>)
        );
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = serviceCollection.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<
            IPasswordHasher<TestTenantUser>
        >();
        var second = secondScope.ServiceProvider.GetRequiredService<
            IPasswordHasher<TestTenantUser>
        >();

        Assert.IsType<NormalizingPasswordHasher<TestTenantUser>>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenTheHostRegisteredATransientHasherFactory_WrapsItAndKeepsItsLifetime()
    {
        var innerHasher = Mocks.Create<IPasswordHasher<TestTenantUser>>();
        innerHasher
            .Setup(x => x.HashPassword(It.IsAny<TestTenantUser>(), ComposedPassword))
            .Returns(MadeHash)
            .Verifiable();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<IPasswordHasher<TestTenantUser>>(_ => innerHasher.Object);
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPasswordHasher<TestTenantUser>)
        );
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);

        using var provider = serviceCollection.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<IPasswordHasher<TestTenantUser>>();
        var second = scope.ServiceProvider.GetRequiredService<IPasswordHasher<TestTenantUser>>();

        Assert.NotSame(first, second);
        var hash = first.HashPassword(new TestTenantUser("alice"), DecomposedPassword);
        Assert.Equal(MadeHash, hash);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenIdentityIsAddedAfterIt_ResolvesTheTenantLookupNormalizer()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(new TestAmbientTenantAccessor());
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();
        serviceCollection.AddIdentityCore<TestTenantUser>();

        using var provider = serviceCollection.BuildServiceProvider();
        var normalizer = provider.GetRequiredService<ILookupNormalizer>();

        Assert.IsType<TenantLookupNormalizer>(normalizer);
    }

    [Fact]
    public void AddAspNetIdentityLocalAccountStore_WhenIdentityIsAddedBeforeIt_ResolvesTheTenantLookupNormalizer()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(new TestAmbientTenantAccessor());
        serviceCollection.AddIdentityCore<TestTenantUser>();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        using var provider = serviceCollection.BuildServiceProvider();
        var normalizer = provider.GetRequiredService<ILookupNormalizer>();

        Assert.IsType<TenantLookupNormalizer>(normalizer);
    }

    #endregion
}
