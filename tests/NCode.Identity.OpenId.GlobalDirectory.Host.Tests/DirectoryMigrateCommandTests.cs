// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryMigrateCommandTests
{
    private const string RuntimeRoleKey = "DirectoryMigration:RuntimeRole";

    public static TheoryData<string[]> UnselectedArguments =>
        new()
        {
            Array.Empty<string>(),
            new[] { "Migrate" },
            new[] { "migrate", "x" },
            new[] { "--migrate" },
        };

    public static TheoryData<string[]> OtherArguments =>
        new()
        {
            Array.Empty<string>(),
            new[] { "Migrate" },
            new[] { "migrate", "x" },
            new[] { "--urls", "http://localhost:5000" },
        };

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            )
            .Build();

    #region CommandName Tests

    [Fact]
    public void CommandName_WhenRead_IsMigrate()
    {
        Assert.Equal("migrate", DirectoryMigrateCommand.CommandName);
    }

    #endregion

    #region IsSelected Tests

    [Fact]
    public void IsSelected_WhenTheArgumentsAreNull_Throws()
    {
        string[] args = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            DirectoryMigrateCommand.IsSelected(args)
        );

        Assert.Equal("args", exception.ParamName);
    }

    [Fact]
    public void IsSelected_WhenTheArgumentsAreExactlyMigrate_ReturnsTrue()
    {
        var result = DirectoryMigrateCommand.IsSelected(["migrate"]);

        Assert.True(result);
    }

    [Theory]
    [MemberData(nameof(UnselectedArguments))]
    public void IsSelected_WhenTheArgumentsAreNotExactlyMigrate_ReturnsFalse(string[] args)
    {
        var result = DirectoryMigrateCommand.IsSelected(args);

        Assert.False(result);
    }

    #endregion

    #region GetHostArguments Tests

    [Fact]
    public void GetHostArguments_WhenTheArgumentsSelectTheCommand_ReturnsNone()
    {
        var result = DirectoryMigrateCommand.GetHostArguments(["migrate"]);

        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(OtherArguments))]
    public void GetHostArguments_WhenTheArgumentsDoNotSelectTheCommand_ReturnsThem(string[] args)
    {
        var result = DirectoryMigrateCommand.GetHostArguments(args);

        Assert.Same(args, result);
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_WhenTheServicesAreNull_Throws()
    {
        IServiceProvider services = null!;
        var configuration = CreateConfiguration((RuntimeRoleKey, "directory_app"));

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await DirectoryMigrateCommand.RunAsync(services, configuration, CancellationToken.None)
        );

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public async Task RunAsync_WhenTheConfigurationIsNull_Throws()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        IConfiguration configuration = null!;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await DirectoryMigrateCommand.RunAsync(services, configuration, CancellationToken.None)
        );

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public async Task RunAsync_WhenTheRuntimeRoleIsNotConfigured_ThrowsNamingTheKeyAndNeverMigrates()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = CreateConfiguration();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DirectoryMigrateCommand.RunAsync(services, configuration, CancellationToken.None)
        );

        Assert.Contains(RuntimeRoleKey, exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
