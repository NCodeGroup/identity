// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.Persistence;
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

    #region ValidateOptionsChange Tests

    private static string? ValidateChange(string currentJson, string proposedJson) =>
        CreateStrategy()
            .ValidateOptionsChange(
                JsonSerializer.Deserialize<JsonElement>(currentJson),
                JsonSerializer.Deserialize<JsonElement>(proposedJson)
            );

    [Fact]
    public void ValidateOptionsChange_WhenTheStoreIsUnchanged_ReturnsNull()
    {
        Assert.Null(ValidateChange("""{"store":"default"}""", """{"store":"default"}"""));
    }

    [Fact]
    public void ValidateOptionsChange_WhenTheStoreChanges_ReturnsDescription()
    {
        Assert.Equal(
            "The store of a local connection cannot change.",
            ValidateChange("""{"store":"default"}""", """{"store":"other"}""")
        );
    }

    #endregion

    #region AddLocalAccountStore Tests

    [Fact]
    public void AddLocalAccountStore_WhenCalledTwice_RegistersOneStrategyAndOneMarker()
    {
        var serviceCollection = new ServiceCollection();

        var storeManagerBuilder = serviceCollection.NewBuilder<StoreManagerLibrary>();

        storeManagerBuilder.AddLocalAccountStore(_ => CreateRegistration("default"));
        var builder = storeManagerBuilder.AddLocalAccountStore(_ => CreateRegistration("other"));

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

    [Fact]
    public async Task AddLocalAccountStore_WhenTwoStoresShareAName_StartingTheHostedServicesThrows()
    {
        var serviceCollection = new ServiceCollection();
        var storeManagerBuilder = serviceCollection.NewBuilder<StoreManagerLibrary>();
        storeManagerBuilder.AddLocalAccountStore(_ => CreateRegistration("default"));
        storeManagerBuilder.AddLocalAccountStore(_ => CreateRegistration("default"));

        await using var serviceProvider = serviceCollection.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var hostedService in serviceProvider.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
        });
    }

    #endregion
}
