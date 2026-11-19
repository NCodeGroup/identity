// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.EntityFramework.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

public sealed class DefaultRegistrationTests
{
    #region AddEntityFrameworkPersistenceServices Tests

    [Fact]
    public void AddEntityFrameworkPersistenceServices_WhenCalled_RegistersAVersion7KeyGenerator()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddEntityFrameworkPersistenceServices<OpenIdDbContext>();

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<ISurrogateKeyGenerator>();
        Assert.Equal(7, generator.CreateKey().Version);
    }

    [Fact]
    public void AddEntityFrameworkPersistenceServices_WhenDataProtectionIsRegistered_ResolvesTheDefaultCursorProtector()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );

        serviceCollection.AddEntityFrameworkPersistenceServices<OpenIdDbContext>();

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var protector = serviceProvider.GetRequiredService<IPageCursorProtector>();
        Assert.IsType<DefaultPageCursorProtector>(protector);
    }

    [Fact]
    public void AddEntityFrameworkPersistenceServices_WhenAGeneratorIsRegisteredFirst_KeepsIt()
    {
        var generator = Mock.Of<ISurrogateKeyGenerator>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(generator);

        serviceCollection.AddEntityFrameworkPersistenceServices<OpenIdDbContext>();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ISurrogateKeyGenerator)
        );
        Assert.Same(generator, descriptor.ImplementationInstance);
    }

    #endregion
}
