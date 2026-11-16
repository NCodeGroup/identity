// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.AwsKms;
using NCode.Identity.Secrets.AwsKms.Options;
using NCode.Identity.Secrets.GoogleCloudKms;
using NCode.Identity.Secrets.GoogleCloudKms.Options;
using NCode.Identity.Server;
using NCode.Identity.SharedState.Valkey;
using NCode.Identity.SharedState.Valkey.Options;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests;

/// <summary>
/// Proves that the identity server's configuration scopes the Valkey and key management service adapters to its cell,
/// whichever host composes the server with them: every Valkey key starts with the cell's name, and a KMS adapter allows
/// only the cell's cloud regions.
/// </summary>
// See ADR-1164 and ADR-1235.
public class CellScopeCompositionTests
{
    // Every test composes the server for the same control-plane cell, which spans one cloud region.
    private static IConfiguration CreateConfiguration(
        params IEnumerable<(string Key, string? Value)> values
    ) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Cell:Name"] = "cell-a",
                    ["Cell:Region"] = "region-a",
                    ["Cell:IsControlPlane"] = "true",
                    ["Cell:CloudRegions:0"] = "eu-west-1",
                }
            )
            .AddInMemoryCollection(values.Select(x => KeyValuePair.Create(x.Key, x.Value)))
            .Build();

    #region AddConfiguration Tests

    [Fact]
    public async Task AddConfiguration_ComposedWithValkey_PrefixesEveryKeyWithTheCellName()
    {
        var configuration = CreateConfiguration(("ConnectionStrings:valkey", "localhost:6399"));
        var serviceCollection = new ServiceCollection();
        var identityServer = serviceCollection.AddIdentityServer();
        identityServer.AddConfiguration(configuration);
        identityServer.SharedStateLibraryBuilder.AddValkey(configuration);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<ValkeyOptions>>().Value;

        Assert.Equal("ncode-identity:cell-a:", options.KeyPrefix);
    }

    [Fact]
    public async Task AddConfiguration_ComposedWithTheAwsKmsKeyWrapper_LimitsItToTheCellsCloudRegions()
    {
        var configuration = CreateConfiguration();
        var serviceCollection = new ServiceCollection();
        var identityServer = serviceCollection.AddIdentityServer();
        identityServer.AddConfiguration(configuration);
        identityServer.SecretPersistenceLibraryBuilder.AddAwsKmsKeyWrapper(configuration);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<AwsKmsKeyWrapperOptions>>().Value;

        Assert.Equal(["eu-west-1"], options.AllowedRegions);
    }

    [Fact]
    public async Task AddConfiguration_ComposedWithTheGoogleCloudKmsRemoteKeyProvider_LimitsItToTheCellsCloudRegions()
    {
        var configuration = CreateConfiguration();
        var serviceCollection = new ServiceCollection();
        var identityServer = serviceCollection.AddIdentityServer();
        identityServer.AddConfiguration(configuration);
        identityServer.SecretsLibraryBuilder.AddGoogleCloudKmsRemoteKeyProvider(configuration);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var options = serviceProvider
            .GetRequiredService<IOptions<GoogleCloudKmsRemoteKeyProviderOptions>>()
            .Value;

        Assert.Equal(["eu-west-1"], options.AllowedLocations);
    }

    #endregion
}
