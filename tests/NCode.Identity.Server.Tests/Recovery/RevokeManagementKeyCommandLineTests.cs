// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Server.Recovery;
using Xunit;

namespace NCode.Identity.Server.Tests.Recovery;

public class RevokeManagementKeyCommandLineTests : BaseTests
{
    private const string KeyId = "management-key-1";

    private static RevokeManagementKeyCommandLine Parse(params string[] args)
    {
        var command = RevokeManagementKeyCommandLine.Parse(args);
        Assert.NotNull(command);
        return command;
    }

    private static ServiceProvider CreateServices(
        IStoreManagerFactory storeManagerFactory,
        bool isControlPlane,
        CapturingLoggerProvider? loggerProvider = null
    )
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            if (loggerProvider is not null)
                builder.AddProvider(loggerProvider);
        });
        serviceCollection.AddSingleton(storeManagerFactory);
        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(
            Options.Create(new CellOptions { IsControlPlane = isControlPlane })
        );
        return serviceCollection.BuildServiceProvider();
    }

    #region Parse Tests

    [Theory]
    [InlineData]
    [InlineData("--urls", "http://localhost")]
    [InlineData("setup")]
    public void Parse_ArgsThatAreNotTheCommand_ReturnNull(params string[] args)
    {
        Assert.Null(RevokeManagementKeyCommandLine.Parse(args));
    }

    [Fact]
    public void Parse_TheCommandWithAKey_ReturnsTheKey()
    {
        var command = Parse("revoke-management-key", KeyId);

        Assert.Null(command.Error);
        Assert.Equal(KeyId, command.KeyId);
    }

    [Theory]
    [InlineData("revoke-management-key")]
    [InlineData("revoke-management-key", " ")]
    [InlineData("revoke-management-key", KeyId, "extra")]
    public void Parse_MissingOrExtraArguments_ReturnACommandWithAnError(params string[] args)
    {
        var command = Parse(args);

        Assert.False(string.IsNullOrWhiteSpace(command.Error));
        Assert.Null(command.KeyId);
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_InvalidArguments_ReturnsTwoWithoutTouchingTheStore()
    {
        var factory = CreateStrictMock<IStoreManagerFactory>();
        using var services = CreateServices(factory.Object, isControlPlane: false);

        var exitCode = await Parse("revoke-management-key")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_TheCellIsTheControlPlane_ReturnsTwoWithoutTouchingTheStore()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var factory = CreateStrictMock<IStoreManagerFactory>();
        using var services = CreateServices(factory.Object, true, loggerProvider);

        var exitCode = await Parse("revoke-management-key", KeyId)
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("management API", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task RunAsync_AWorkloadCell_RecordsTheKeyAsRevokedOffline()
    {
        PersistedRevokedManagementKey? recorded = null;
        var store = CreateStrictMock<IReplicatedDocumentStore>();
        store
            .Setup(x =>
                x.AddRevokedKeyAsync(
                    It.IsAny<PersistedRevokedManagementKey>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<PersistedRevokedManagementKey, CancellationToken>((key, _) => recorded = key)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var storeManager = CreateStrictMock<IStoreManager>();
        storeManager.Setup(x => x.GetStore<IReplicatedDocumentStore>()).Returns(store.Object);
        storeManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        storeManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var factory = CreateStrictMock<IStoreManagerFactory>();
        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IStoreManager>(storeManager.Object))
            .Verifiable();
        using var services = CreateServices(factory.Object, isControlPlane: false);

        var exitCode = await Parse("revoke-management-key", KeyId)
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(recorded);
        Assert.Equal(KeyId, recorded.KeyId);
        Assert.Equal(PersistedRevokedManagementKey.OfflineSource, recorded.Source);
    }

    [Fact]
    public async Task RunAsync_TheStoreFails_ReturnsOne()
    {
        var factory = CreateStrictMock<IStoreManagerFactory>();
        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("The database is down."))
            .Verifiable();
        using var services = CreateServices(factory.Object, isControlPlane: false);

        var exitCode = await Parse("revoke-management-key", KeyId)
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    #endregion
}
