// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Tenants.Keys;

public sealed class DefaultTenantKeyCustodyGuardTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private const string OtherCell = "cell-2";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }

    private Mock<IStoreManager> MockStoreManager { get; }

    private Mock<ITenantStore> MockTenantStore { get; }

    private Mock<ITenantDirectory> MockTenantDirectory { get; }

    private FakeLogger<DefaultTenantKeyCustodyGuard> Logger { get; } = new();

    private DefaultTenantKeyCustodyGuard Guard { get; }

    public DefaultTenantKeyCustodyGuardTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTenantStore = CreateStrictMock<ITenantStore>();
        MockTenantDirectory = CreateStrictMock<ITenantDirectory>();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // The guard never calls the directory, so every member of this strict mock fails the test when called.
        Guard = new DefaultTenantKeyCustodyGuard(MockStoreManagerFactory.Object, Logger);
    }

    #region Scaffolding

    private void SetupTenant(string? keyCustody, string? keyCustodyCell = null)
    {
        PersistedTenant? tenant = null;
        if (keyCustody is not null)
        {
            tenant = new PersistedTenantBuilder { TenantId = TenantId }.Build();
            tenant.KeyCustody = keyCustody;
            tenant.KeyCustodyCell = keyCustodyCell;
        }

        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, CancellationToken.None))
            .ReturnsAsync(tenant)
            .Verifiable();
    }

    #endregion

    #region MayChangeKeysAsync Tests

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheCustodyIsHeld_ReturnsTrue()
    {
        SetupTenant(TenantKeyCustody.Held);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.True(actual);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Theory]
    [InlineData(TenantKeyCustody.Outgoing)]
    [InlineData(TenantKeyCustody.Incoming)]
    public async Task MayChangeKeysAsync_WhenTheCustodyIsFenced_ReturnsFalseAndLogs(
        string keyCustody
    )
    {
        SetupTenant(keyCustody, OtherCell);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains(TenantId, record.Message);
        Assert.Contains(keyCustody, record.Message);
        Assert.Contains(OtherCell, record.Message);
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheCustodyIsFencedWithoutACell_ReturnsFalseAndLogsWithoutACell()
    {
        SetupTenant(TenantKeyCustody.Incoming);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains(TenantId, record.Message);
        Assert.Contains(TenantKeyCustody.Incoming, record.Message);
        Assert.DoesNotContain("cell '", record.Message);
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheTenantIsMissing_ReturnsFalseAndLogs()
    {
        SetupTenant(null);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains(TenantId, record.Message);
    }

    [Fact]
    public async Task MayChangeKeysAsync_Always_NeverCallsTheDirectory()
    {
        SetupTenant(TenantKeyCustody.Outgoing, OtherCell);

        await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        MockTenantDirectory.VerifyNoOtherCalls();
    }

    #endregion
}
