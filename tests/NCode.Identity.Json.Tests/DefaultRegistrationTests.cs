// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;

namespace NCode.Identity.Json.Tests;

public class DefaultRegistrationTests
{
    #region AddPersistedPayloadSerializer Tests

    [Fact]
    public void AddPersistedPayloadSerializer_WhenCalled_RegistersTheDefaultSerializerAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddPersistedPayloadSerializer();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPersistedPayloadSerializer)
        );
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DefaultPersistedPayloadSerializer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddPersistedPayloadSerializer_WhenASerializerIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IPersistedPayloadSerializer>(_ =>
            throw new InvalidOperationException("The test never resolves the serializer.")
        );

        serviceCollection.AddPersistedPayloadSerializer();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPersistedPayloadSerializer)
        );
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Null(descriptor.ImplementationType);
    }

    [Fact]
    public void AddPersistedPayloadSerializer_WhenCalledTwice_RegistersOneSerializer()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddPersistedPayloadSerializer();
        serviceCollection.AddPersistedPayloadSerializer();

        Assert.Single(serviceCollection, x => x.ServiceType == typeof(IPersistedPayloadSerializer));
    }

    #endregion
}
