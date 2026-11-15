// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Host;
using NCode.Identity.SharedState.Valkey.Options;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Host;

/// <summary>
/// Provides unit tests for the <see cref="CellValkeyOptionsSetup"/> class and the
/// <see cref="CellOptionsSetupRegistration"/> class.
/// </summary>
public class CellValkeyOptionsSetupTests
{
    private static CellValkeyOptionsSetup CreateSetup(string cellName) =>
        new(Options.Create(new CellOptions { Name = cellName }));

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_DefaultPrefix_AppendsCell()
    {
        var options = new ValkeyOptions();

        CreateSetup("cell-a").PostConfigure(name: null, options);

        Assert.Equal("ncode-identity:cell-a:", options.KeyPrefix);
    }

    [Fact]
    public void PostConfigure_ConfiguredPrefix_KeepsItAndAppendsCell()
    {
        var options = new ValkeyOptions { KeyPrefix = "acme:" };

        CreateSetup("cell-a").PostConfigure(name: null, options);

        Assert.Equal("acme:cell-a:", options.KeyPrefix);
    }

    [Fact]
    public void PostConfigure_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("cell-a").PostConfigure(name: null, null!)
        );
    }

    #endregion

    #region AddCellOptionsSetups Tests

    [Fact]
    public void AddCellOptionsSetups_Registers_FiveSetups()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddCellOptionsSetups();

        Assert.Equal(5, CountSetups(serviceCollection));
        Assert.Single(
            serviceCollection,
            x =>
                x.ServiceType == typeof(IPostConfigureOptions<ValkeyOptions>)
                && x.ImplementationType == typeof(CellValkeyOptionsSetup)
        );
        Assert.Equal(
            4,
            serviceCollection.Count(x => x.ImplementationType == typeof(CellKmsRegionOptionsSetup))
        );
    }

    [Fact]
    public void AddCellOptionsSetups_CalledTwice_AddsNoneTheSecondTime()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddCellOptionsSetups();

        serviceCollection.AddCellOptionsSetups();

        Assert.Equal(5, CountSetups(serviceCollection));
    }

    private static int CountSetups(IServiceCollection serviceCollection) =>
        serviceCollection.Count(x =>
            x.ImplementationType == typeof(CellValkeyOptionsSetup)
            || x.ImplementationType == typeof(CellKmsRegionOptionsSetup)
        );

    #endregion
}
