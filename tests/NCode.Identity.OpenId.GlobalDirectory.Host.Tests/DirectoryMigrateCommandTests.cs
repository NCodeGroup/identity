// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory.Server;
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
    public async Task RunAsync_WhenTheRuntimeRoleIsNotConfigured_ReturnsOneAndLogsTheKey()
    {
        await using var services = new ServiceCollection().AddFakeLogging().BuildServiceProvider();
        var configuration = CreateConfiguration();

        var exitCode = await DirectoryMigrateCommand.RunAsync(
            services,
            configuration,
            CancellationToken.None
        );

        Assert.Equal(1, exitCode);
        var record = Assert.Single(services.GetFakeLogCollector().GetSnapshot());
        Assert.Equal(Logging.EventIds.DirectoryMigrateCommandFailed, record.Id.Id);
        var exception = Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(RuntimeRoleKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenTheCallerCancels_PropagatesWithoutLogging()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var cancellationToken = cancellationTokenSource.Token;
        var cancellation = new OperationCanceledException(cancellationToken);
        await using var services = new ServiceCollection()
            .AddFakeLogging()
            .AddSingleton<IDbContextFactory<DirectoryDbContext>>(
                new FailingDbContextFactory(cancellation)
            )
            .BuildServiceProvider();
        var configuration = CreateConfiguration((RuntimeRoleKey, "directory_app"));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await DirectoryMigrateCommand.RunAsync(services, configuration, cancellationToken)
        );

        Assert.Same(cancellation, thrown);
        Assert.Empty(services.GetFakeLogCollector().GetSnapshot());
    }

    [Fact]
    public async Task RunAsync_WhenTheDatabaseFailsAfterTheCallerCancels_ReturnsOneAndLogsTheFailure()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var cancellationToken = cancellationTokenSource.Token;
        var failure = new InvalidOperationException("The database is unreachable.");
        await using var services = new ServiceCollection()
            .AddFakeLogging()
            .AddSingleton<IDbContextFactory<DirectoryDbContext>>(
                new FailingDbContextFactory(failure)
            )
            .BuildServiceProvider();
        var configuration = CreateConfiguration((RuntimeRoleKey, "directory_app"));

        var exitCode = await DirectoryMigrateCommand.RunAsync(
            services,
            configuration,
            cancellationToken
        );

        Assert.Equal(1, exitCode);
        var record = Assert.Single(services.GetFakeLogCollector().GetSnapshot());
        Assert.Equal(Logging.EventIds.DirectoryMigrateCommandFailed, record.Id.Id);
        Assert.Same(failure, record.Exception);
    }

    #endregion

    /// <summary>
    /// A <see cref="IDbContextFactory{TContext}"/> whose asynchronous creation fails with a given exception.
    /// </summary>
    /// <param name="exception">The exception that <see cref="CreateDbContextAsync"/> fails with.</param>
    private sealed class FailingDbContextFactory(Exception exception)
        : IDbContextFactory<DirectoryDbContext>
    {
        /// <inheritdoc />
        public DirectoryDbContext CreateDbContext() => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<DirectoryDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromException<DirectoryDbContext>(exception);
    }
}
