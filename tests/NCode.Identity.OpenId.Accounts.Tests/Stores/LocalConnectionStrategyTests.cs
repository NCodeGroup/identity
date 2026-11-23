// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Accounts.Stores;

public sealed class LocalConnectionStrategyTests
{
    private static LocalAccountStoreRegistration CreateRegistration(string name) =>
        new()
        {
            Name = name,
            CreateStore = _ => throw new NotSupportedException(),
            CreateTransferStore = _ => throw new NotSupportedException(),
        };

    private static LocalConnectionStrategy CreateStrategy() =>
        new(new LocalAccountStoreCatalog([CreateRegistration("default")]));

    private static string? Validate(string optionsJson)
    {
        var options = JsonSerializer.Deserialize<JsonElement>(optionsJson);
        return CreateStrategy().ValidateOptions(options);
    }

    #region Properties Tests

    [Fact]
    public void Properties_ReturnLocalPasswordStrategy()
    {
        var strategy = CreateStrategy();

        Assert.Equal(ConnectionStrategyNames.Local, strategy.Name);
        Assert.Equal(ConnectionSignInMethods.Password, strategy.SignInMethod);
    }

    #endregion

    #region ValidateOptions Tests

    [Fact]
    public void ValidateOptions_WhenStoreIsRegistered_ReturnsNull()
    {
        Assert.Null(Validate("""{"store":"default"}"""));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"store":1}""")]
    [InlineData("""{"store":"missing"}""")]
    public void ValidateOptions_WhenInvalid_ReturnsDescription(string optionsJson)
    {
        Assert.False(string.IsNullOrEmpty(Validate(optionsJson)));
    }

    #endregion

    #region AddLocalAccountStore Tests

    [Fact]
    public void AddLocalAccountStore_WhenCalledTwice_RegistersOneStrategyAndOneMarker()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLocalAccountStore(_ => CreateRegistration("default"));
        var builder = serviceCollection.AddLocalAccountStore(_ => CreateRegistration("other"));

        Assert.Same(serviceCollection, builder.ServiceCollection);
        Assert.True(serviceCollection.IsRegistered<LocalAccountLibrary>());
        Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IConnectionStrategy)
        );
        Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IRegistrationMarker<LocalAccountLibrary>)
        );

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var catalog = serviceProvider.GetRequiredService<LocalAccountStoreCatalog>();
        Assert.True(catalog.Contains("default"));
        Assert.True(catalog.Contains("other"));
    }

    #endregion
}
