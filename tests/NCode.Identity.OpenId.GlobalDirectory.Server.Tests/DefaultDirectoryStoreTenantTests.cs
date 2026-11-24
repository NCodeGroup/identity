// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreTenantTests : DefaultDirectoryStoreTestBase
{
    private async ValueTask RegisterCellAsync(CancellationToken cancellationToken = default)
    {
        await ReportCellAsync(
            new CellDescriptor
            {
                Name = CellName,
                Region = CellRegion,
                Kind = CellKinds.Shared,
                ManagementUrl = "https://cell-a.example/",
            },
            cancellationToken
        );
    }

    private ValueTask<ReservationResult> ChangeIssuerAsync(
        string tenantId,
        string? previousIssuer,
        string? issuer,
        CancellationToken cancellationToken = default
    ) =>
        Tenants.ChangeIssuerAsync(
            new TenantIssuerChange
            {
                TenantId = tenantId,
                PreviousIssuer = previousIssuer,
                Issuer = issuer,
            },
            cancellationToken
        );

    private ValueTask<DirectoryTenantWriteResult> WriterChangeIssuerAsync(
        string callerCell,
        string tenantId,
        string? previousIssuer,
        string? issuer,
        CancellationToken cancellationToken = default
    ) =>
        TenantWriter.ChangeIssuerAsync(
            new TenantIssuerChange
            {
                TenantId = tenantId,
                PreviousIssuer = previousIssuer,
                Issuer = issuer,
            },
            callerCell,
            cancellationToken
        );

    private ValueTask<ReservationResult> PlaceTenantAsync(
        string tenantId,
        string issuer,
        string cell = CellName,
        CancellationToken cancellationToken = default
    ) =>
        Tenants.PlaceAsync(
            new TenantPlacementRequest
            {
                TenantId = tenantId,
                Issuer = issuer,
                Cell = cell,
            },
            cancellationToken
        );

    #region PlaceAsync Tests

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsReserved_PlacesItInTheCell()
    {
        await RegisterCellAsync();
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await PlaceTenantAsync(TenantId, Issuer);

        Assert.True(result.IsReserved);
        var placement = await Tenants.GetPlacementOrDefaultAsync(
            TenantId.ToUpperInvariant(),
            CancellationToken.None
        );
        Assert.NotNull(placement);
        Assert.Equal(TenantId, placement.TenantId);
        Assert.Equal(Issuer, placement.Issuer);
        Assert.Equal(CellName, placement.Cell);
        Assert.Equal(CellRegion, placement.Region);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StatePlaced, record.State);
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsNotReserved_ConflictsAndRecordsNothing()
    {
        await RegisterCellAsync();

        var result = await PlaceTenantAsync(TenantId, Issuer);

        Assert.False(result.IsReserved);
        Assert.Equal($"The tenant '{TenantId}' is not reserved.", result.ConflictDetail);
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
        Assert.Null(await ReadTenantAsync(TenantId));
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsRetired_ConflictsAndKeepsItRetired()
    {
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            CancellationToken.None
        );

        var result = await PlaceTenantAsync(TenantId, Issuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The tenant identifier '{TenantId}' belonged to a deleted tenant and is never reissued.",
            result.ConflictDetail
        );
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsReservedForAnotherCell_Conflicts()
    {
        await RegisterCellAsync();
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await PlaceTenantAsync(TenantId, Issuer, "cell-b");

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The tenant '{TenantId}' is reserved for another cell or issuer.",
            result.ConflictDetail
        );
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateReserved, record.State);
        Assert.Equal(CellName, record.Cell);
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsReservedForAnotherIssuer_Conflicts()
    {
        await RegisterCellAsync();
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await PlaceTenantAsync(TenantId, OtherIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The tenant '{TenantId}' is reserved for another cell or issuer.",
            result.ConflictDetail
        );
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateReserved, record.State);
        Assert.Equal(Issuer, record.Issuer);
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantHasNoIssuer_PlacesItWithoutAnIssuer()
    {
        await RegisterCellAsync();

        await ReserveAndPlaceAsync(TenantId, issuer: null);

        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(placement);
        Assert.Null(placement.Issuer);
        Assert.Equal(CellName, placement.Cell);
    }

    [Fact]
    public async Task PlaceAsync_WhenATenantReservedWithoutAnIssuerIsPlacedUnderOne_Conflicts()
    {
        await RegisterCellAsync();
        Assert.True((await ReserveAsync(TenantId, issuer: null)).IsReserved);

        var result = await PlaceTenantAsync(TenantId, Issuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The tenant '{TenantId}' is reserved for another cell or issuer.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsAlreadyPlaced_IsReserved()
    {
        await RegisterCellAsync();
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await PlaceTenantAsync(TenantId, Issuer);

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StatePlaced, record.State);
    }

    #endregion

    #region GetPlacementOrDefaultAsync Tests

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenTheCellIsNotRegistered_ReturnsAnEmptyRegion()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);

        Assert.NotNull(placement);
        Assert.Equal(CellName, placement.Cell);
        Assert.Equal(string.Empty, placement.Region);
    }

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenTheTenantIsUnknown_ReturnsNull()
    {
        var placement = await Tenants.GetPlacementOrDefaultAsync("unknown", CancellationToken.None);

        Assert.Null(placement);
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task ReleaseAsync_WhenTheTenantIsReserved_FreesTheIdentifierAndIssuer()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        await Tenants.ReleaseAsync(TenantId, CancellationToken.None);

        Assert.Null(await ReadTenantAsync(TenantId));
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheTenantIsPlaced_KeepsTheTenant()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        await Tenants.ReleaseAsync(TenantId, CancellationToken.None);

        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StatePlaced, record.State);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheTenantIsUnknown_DoesNothing()
    {
        await Tenants.ReleaseAsync("unknown", CancellationToken.None);

        Assert.Null(await ReadTenantAsync("unknown"));
    }

    [Fact]
    public async Task ReleaseAsync_WhenAnotherCellReleasesAnUnknownTenant_IsDoneAndWritesNothing()
    {
        var result = await TenantWriter.ReleaseAsync("unknown", "cell-b", CancellationToken.None);

        Assert.Equal(DirectoryTenantWriteOutcome.Done, result.Outcome);
        Assert.Null(await ReadTenantAsync("unknown"));
    }

    [Fact]
    public async Task ReleaseAsync_WhenAnotherCellReleasesAReservedTenant_ForbidsAndKeepsTheRecord()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await TenantWriter.ReleaseAsync(TenantId, "cell-b", CancellationToken.None);

        Assert.Equal(DirectoryTenantWriteOutcome.Forbidden, result.Outcome);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateReserved, record.State);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheControlPlaneReleasesAReservedTenant_FreesTheIdentifier()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await TenantWriter.ReleaseAsync(
            TenantId,
            ControlPlaneCell,
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Done, result.Outcome);
        Assert.Null(await ReadTenantAsync(TenantId));
    }

    #endregion

    #region RetireAsync Tests

    [Fact]
    public async Task RetireAsync_WhenTheTenantIsPlaced_RetiresTheIdentifierAndTheIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        Assert.True(
            await Tenants.IsTenantIdRetiredAsync(
                TenantId.ToUpperInvariant(),
                CancellationToken.None
            )
        );
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(
                Issuer.ToUpperInvariant(),
                CancellationToken.None
            )
        );
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateRetired, record.State);
        Assert.Null(record.Issuer);
        Assert.Null(record.NormalizedIssuer);
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_WhenTheTenantIsUnknown_RetiresTheIdentifier()
    {
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            CancellationToken.None
        );

        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RetireAsync_WhenTheTenantIsRetiredTwice_Succeeds()
    {
        var retirement = new TenantRetirement { TenantId = TenantId, Issuer = Issuer };
        await Tenants.RetireAsync(retirement, CancellationToken.None);

        await Tenants.RetireAsync(retirement, CancellationToken.None);

        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_WhenTheTenantIsRetired_KeepsTheIssuerRetiredForOtherTenants()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        var result = await ReserveAsync(OtherTenantId, Issuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The issuer '{Issuer}' belonged to another tenant and is never reissued.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task RetireAsync_WithoutAnIssuer_RetiresTheStoredIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            CancellationToken.None
        );

        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
        var result = await ReserveAsync(OtherTenantId, Issuer);
        Assert.False(result.IsReserved);
    }

    [Fact]
    public async Task RetireAsync_WhenAnotherCellRetiresAnUnknownTenant_ForbidsAndWritesNothing()
    {
        var result = await TenantWriter.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            "cell-b",
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Forbidden, result.Outcome);
        Assert.Null(await ReadTenantAsync(TenantId));
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RetireAsync_WhenTheControlPlaneRetiresAnUnknownTenant_RetiresTheIdentifierAndTheNamedIssuer()
    {
        var result = await TenantWriter.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            ControlPlaneCell,
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Done, result.Outcome);
        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RetireAsync_WhenTheHolderNamesAForeignIssuer_RetiresOnlyTheStoredIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await TenantWriter.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = OtherIssuer },
            CellName,
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Done, result.Outcome);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(OtherIssuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RetireAsync_WhenAnotherCellRetiresAHeldTenant_ForbidsAndKeepsTheRecord()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await TenantWriter.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            "cell-b",
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Forbidden, result.Outcome);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StatePlaced, record.State);
    }

    #endregion

    #region IsTenantIdRetiredAsync Tests

    [Fact]
    public async Task IsTenantIdRetiredAsync_WhenTheTenantIsReserved_ReturnsFalse()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var retired = await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None);

        Assert.False(retired);
    }

    #endregion

    #region GetRetiredIssuerHolderOrDefaultAsync Tests

    [Fact]
    public async Task GetRetiredIssuerHolderOrDefaultAsync_WhenTheIssuerIsHeldByALiveTenant_ReturnsNull()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var holder = await Tenants.GetRetiredIssuerHolderOrDefaultAsync(
            Issuer,
            CancellationToken.None
        );

        Assert.Null(holder);
    }

    #endregion

    #region ChangeIssuerAsync Tests

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheNewIssuerIsFree_UpdatesTheRecordAndRetiresThePreviousIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await ChangeIssuerAsync(TenantId, Issuer, FreeIssuer);

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(FreeIssuer, record.Issuer);
        Assert.Equal(FreeIssuer, record.NormalizedIssuer);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(placement);
        Assert.Equal(FreeIssuer, placement.Issuer);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenAnotherTenantHoldsTheIssuer_ConflictsWithTheHeldIssuerText()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await ReserveAndPlaceAsync(OtherTenantId, OtherIssuer);

        var result = await ChangeIssuerAsync(TenantId, Issuer, OtherIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the issuer '{OtherIssuer}'.",
            result.ConflictDetail
        );
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(Issuer, record.Issuer);
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenAnotherTenantRetiredTheIssuer_ConflictsWithTheRetiredIssuerText()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = OtherTenantId, Issuer = OtherIssuer },
            CancellationToken.None
        );

        var result = await ChangeIssuerAsync(TenantId, Issuer, OtherIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The issuer '{OtherIssuer}' belonged to another tenant and is never reissued.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantTakesBackItsOwnIssuer_Succeeds()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        Assert.True((await ChangeIssuerAsync(TenantId, Issuer, FreeIssuer)).IsReserved);

        var result = await ChangeIssuerAsync(TenantId, FreeIssuer, Issuer.ToUpperInvariant());

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(Issuer.ToUpperInvariant(), record.Issuer);
        Assert.Equal(Issuer, record.NormalizedIssuer);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(FreeIssuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheIssuerIsUnchanged_RetiresNothing()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await ChangeIssuerAsync(TenantId, Issuer, Issuer.ToUpperInvariant());

        Assert.True(result.IsReserved);
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantLosesItsIssuer_ClearsTheRecordAndRetiresThePreviousIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await ChangeIssuerAsync(TenantId, Issuer, issuer: null);

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Null(record.Issuer);
        Assert.Null(record.NormalizedIssuer);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantIsUnknown_ReportsNotFoundAndRetiresNothing()
    {
        var result = await TenantWriter.ChangeIssuerAsync(
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = Issuer,
                Issuer = FreeIssuer,
            },
            ControlPlaneCell,
            CancellationToken.None
        );

        Assert.Equal(DirectoryTenantWriteOutcome.NotFound, result.Outcome);
        Assert.Null(await ReadTenantAsync(TenantId));
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantIsUnknownAndTheCallerIsAnotherCell_ForbidsAndWritesNothing()
    {
        var result = await WriterChangeIssuerAsync("cell-b", TenantId, Issuer, FreeIssuer);

        Assert.Equal(DirectoryTenantWriteOutcome.Forbidden, result.Outcome);
        Assert.Null(await ReadTenantAsync(TenantId));
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheCallerIsACellThatDoesNotHoldTheTenant_Forbids()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await WriterChangeIssuerAsync("cell-b", TenantId, Issuer, FreeIssuer);

        Assert.Equal(DirectoryTenantWriteOutcome.Forbidden, result.Outcome);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(Issuer, record.Issuer);
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenThePreviousIssuerIsNotTheStoredIssuer_ConflictsAndRetiresNothing()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await WriterChangeIssuerAsync(CellName, TenantId, OtherIssuer, FreeIssuer);

        Assert.Equal(DirectoryTenantWriteOutcome.Conflict, result.Outcome);
        Assert.Equal(
            $"The tenant '{TenantId}' does not hold the issuer '{OtherIssuer}'.",
            result.Detail
        );
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(Issuer, record.Issuer);
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(OtherIssuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheChangeIsRetriedAfterItCommitted_IsDone()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        var first = await WriterChangeIssuerAsync(CellName, TenantId, Issuer, FreeIssuer);

        var retry = await WriterChangeIssuerAsync(CellName, TenantId, Issuer, FreeIssuer);

        Assert.Equal(DirectoryTenantWriteOutcome.Done, first.Outcome);
        Assert.Equal(DirectoryTenantWriteOutcome.Done, retry.Outcome);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(FreeIssuer, record.Issuer);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantIsRetired_ConflictsAndKeepsTheRecordWithoutAnIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        var result = await ChangeIssuerAsync(TenantId, previousIssuer: null, FreeIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal($"The tenant '{TenantId}' is retired.", result.ConflictDetail);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateRetired, record.State);
        Assert.Null(record.Issuer);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheControlPlaneCellChangesItsRootTenantsIssuer_ChangesIt()
    {
        var result = await WriterChangeIssuerAsync(
            ControlPlaneCell,
            ControlPlaneTenantId,
            ControlPlaneIssuer,
            FreeIssuer
        );

        Assert.Equal(DirectoryTenantWriteOutcome.Done, result.Outcome);
        var record = await ReadTenantAsync(ControlPlaneTenantId);
        Assert.NotNull(record);
        Assert.Equal(FreeIssuer, record.Issuer);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenATenantTakesTheControlPlaneIssuer_Conflicts()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await ChangeIssuerAsync(TenantId, Issuer, ControlPlaneIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the issuer '{ControlPlaneIssuer}'.",
            result.ConflictDetail
        );
    }

    #endregion
}
