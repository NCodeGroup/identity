// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace NCode.Identity.Persistence;

public class DefaultRegistrationTests
{
    #region AddSurrogateKeyGenerator Tests

    [Fact]
    public void AddSurrogateKeyGenerator_WhenCalled_RegistersASingletonThatMintsVersion7Keys()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSurrogateKeyGenerator();

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var first = serviceProvider.GetRequiredService<ISurrogateKeyGenerator>();
        var second = serviceProvider.GetRequiredService<ISurrogateKeyGenerator>();

        Assert.Same(first, second);

        var firstKey = first.CreateKey();
        var secondKey = first.CreateKey();

        Assert.NotEqual(firstKey, secondKey);
        Assert.Equal(7, firstKey.Version);
        Assert.Equal(7, secondKey.Version);
    }

    [Fact]
    public void AddSurrogateKeyGenerator_WhenAGeneratorIsRegisteredFirst_KeepsIt()
    {
        var mockGenerator = new Mock<ISurrogateKeyGenerator>(MockBehavior.Strict);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockGenerator.Object);

        serviceCollection.AddSurrogateKeyGenerator();

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<ISurrogateKeyGenerator>();

        Assert.Same(mockGenerator.Object, generator);
    }

    [Fact]
    public void AddSurrogateKeyGenerator_WhenCalledTwice_RegistersOneGenerator()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSurrogateKeyGenerator();
        serviceCollection.AddSurrogateKeyGenerator();

        Assert.Single(serviceCollection, x => x.ServiceType == typeof(ISurrogateKeyGenerator));
    }

    #endregion
}
