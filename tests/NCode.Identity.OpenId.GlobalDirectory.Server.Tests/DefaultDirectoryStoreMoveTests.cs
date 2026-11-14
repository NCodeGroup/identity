// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreMoveTests : DefaultDirectoryStoreTestBase
{
    private const string TargetCellName = "cell-b";
    private const string OtherRegion = "region-b";

    private async ValueTask RegisterCellAsync(
        string name,
        string region,
        CancellationToken cancellationToken = default
    ) =>
        await Cells.RegisterAsync(
            new CellDescriptor
            {
                Name = name,
                Region = region,
                Kind = CellKinds.Shared,
                ManagementUrl = $"https://{name}.example/",
            },
            cancellationToken
        );

    #region MoveAsync Tests

    [Fact]
    public async Task MoveAsync_WhenPlacedInTheSourceCellAndBothCellsShareARegion_MovesThePlacement()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await RegisterCellAsync(TargetCellName, CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);
        TimeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.True(result.IsMoved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StatePlaced, record.State);
        Assert.Equal(TargetCellName, record.Cell);
        Assert.Equal(TimeProvider.GetUtcNow(), record.ModifiedWhen);
        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(placement);
        Assert.Equal(TargetCellName, placement.Cell);
        Assert.Equal(CellRegion, placement.Region);
        Assert.Equal(Issuer, placement.Issuer);
    }

    [Fact]
    public async Task MoveAsync_WhenPlacedInAnotherCell_ConflictsAndKeepsThePlacement()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await RegisterCellAsync(TargetCellName, CellRegion);
        await RegisterCellAsync("cell-c", CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await Tenants.MoveAsync(
            TenantId,
            "cell-c",
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal(
            $"The tenant '{TenantId}' is not placed in the cell 'cell-c'.",
            result.ConflictDetail
        );
        Assert.Equal(CellName, (await ReadTenantAsync(TenantId))?.Cell);
    }

    [Fact]
    public async Task MoveAsync_WhenTheTenantIsOnlyReserved_Conflicts()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await RegisterCellAsync(TargetCellName, CellRegion);
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal($"The tenant '{TenantId}' is not placed in a cell.", result.ConflictDetail);
        Assert.Equal(TenantRecord.StateReserved, (await ReadTenantAsync(TenantId))?.State);
    }

    [Fact]
    public async Task MoveAsync_WhenTheTenantIsUnknown_Conflicts()
    {
        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal($"The tenant '{TenantId}' is not placed in a cell.", result.ConflictDetail);
    }

    [Fact]
    public async Task MoveAsync_WhenTheCellsAreInAnotherRegion_ConflictsAndKeepsThePlacement()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await RegisterCellAsync(TargetCellName, OtherRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal(
            $"The cells '{CellName}' and '{TargetCellName}' are in different regions, and a tenant moves only within its region.",
            result.ConflictDetail
        );
        Assert.Equal(CellName, (await ReadTenantAsync(TenantId))?.Cell);
    }

    [Fact]
    public async Task MoveAsync_WhenTheTargetCellIsNotRegistered_Conflicts()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal($"The cell '{TargetCellName}' is not registered.", result.ConflictDetail);
        Assert.Equal(CellName, (await ReadTenantAsync(TenantId))?.Cell);
    }

    [Fact]
    public async Task MoveAsync_WhenTheSourceCellIsNotRegistered_Conflicts()
    {
        await RegisterCellAsync(TargetCellName, CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal($"The cell '{CellName}' is not registered.", result.ConflictDetail);
    }

    [Fact]
    public async Task MoveAsync_WhenTheTargetIsTheSourceCell_Conflicts()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await Tenants.MoveAsync(TenantId, CellName, CellName, CancellationToken.None);

        Assert.False(result.IsMoved);
        Assert.Equal(
            $"The tenant '{TenantId}' is already placed in the cell '{CellName}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task MoveAsync_WhenTheTenantIsRetired_Conflicts()
    {
        await RegisterCellAsync(CellName, CellRegion);
        await RegisterCellAsync(TargetCellName, CellRegion);
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        var result = await Tenants.MoveAsync(
            TenantId,
            CellName,
            TargetCellName,
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal(TenantRecord.StateRetired, (await ReadTenantAsync(TenantId))?.State);
    }

    #endregion
}
