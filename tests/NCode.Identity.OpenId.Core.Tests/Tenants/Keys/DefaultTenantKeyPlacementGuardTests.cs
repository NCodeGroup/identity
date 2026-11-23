// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Tenants.Keys;

public sealed class DefaultTenantKeyPlacementGuardTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private const string ThisCell = "cell-1";

    private const string OtherCell = "cell-2";

    private Mock<ITenantDirectory> MockTenantDirectory { get; }

    private Mock<ITenantResolver> MockTenantResolver { get; }

    private FakeLogger<DefaultTenantKeyPlacementGuard> Logger { get; } = new();

    private DefaultTenantKeyPlacementGuard Guard { get; }

    public DefaultTenantKeyPlacementGuardTests()
    {
        MockTenantDirectory = CreateStrictMock<ITenantDirectory>();
        MockTenantResolver = CreateStrictMock<ITenantResolver>();
        Guard = CreateGuard(new CellOptions { Name = ThisCell });
    }

    #region Scaffolding

    private DefaultTenantKeyPlacementGuard CreateGuard(CellOptions cellOptions) =>
        new(
            MockTenantDirectory.Object,
            MockTenantResolver.Object,
            Options.Create(cellOptions),
            Logger
        );

    private void SetupPlacement(string? cell)
    {
        var placement = cell is null
            ? null
            : new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = "https://issuer.example.com",
                Cell = cell,
                Region = "region-1",
            };

        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None))
            .ReturnsAsync(placement)
            .Verifiable();
    }

    #endregion

    #region MayChangeKeysAsync Tests

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheControlPlaneCellAsksForItsControlPlaneTenant_ReturnsTrueWithoutTheDirectory()
    {
        var guard = CreateGuard(new CellOptions { Name = ThisCell, IsControlPlane = true });
        MockTenantResolver.Setup(x => x.GetControlPlaneTenantId()).Returns(TenantId).Verifiable();

        var actual = await guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.True(actual);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheControlPlaneCellAsksForAnotherTenant_AsksTheDirectory()
    {
        var guard = CreateGuard(new CellOptions { Name = ThisCell, IsControlPlane = true });
        MockTenantResolver.Setup(x => x.GetControlPlaneTenantId()).Returns("root").Verifiable();
        SetupPlacement(OtherCell);

        var actual = await guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheTenantHasNoPlacement_ReturnsTrue()
    {
        SetupPlacement(null);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.True(actual);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheDirectoryPlacesTheTenantInThisCell_ReturnsTrue()
    {
        SetupPlacement(ThisCell);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.True(actual);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheDirectoryPlacesTheTenantInAnotherCell_ReturnsFalseAndLogs()
    {
        SetupPlacement(OtherCell);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains(TenantId, record.Message);
        Assert.Contains(OtherCell, record.Message);
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheCellNamesDifferOnlyInCase_ReturnsFalseAndLogs()
    {
        var upperCaseCell = ThisCell.ToUpperInvariant();
        SetupPlacement(upperCaseCell);

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
    }

    [Fact]
    public async Task MayChangeKeysAsync_WhenTheDirectoryIsUnavailable_ReturnsFalseAndLogs()
    {
        var exception = new DirectoryUnavailableException("The directory is unavailable.");
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None))
            .Returns(() => throw exception)
            .Verifiable();

        var actual = await Guard.MayChangeKeysAsync(TenantId, CancellationToken.None);

        Assert.False(actual);
        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Same(exception, record.Exception);
        Assert.Contains(TenantId, record.Message);
    }

    #endregion
}
