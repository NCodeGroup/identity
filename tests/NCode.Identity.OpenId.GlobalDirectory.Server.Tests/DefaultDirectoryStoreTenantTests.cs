// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreTenantTests : DefaultDirectoryStoreTestBase
{
    private async ValueTask RegisterCellAsync(CancellationToken cancellationToken = default)
    {
        await Cells.RegisterAsync(
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

    #region PlaceAsync Tests

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsReserved_PlacesItInTheCell()
    {
        await RegisterCellAsync();
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        await Tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = Issuer,
                Cell = CellName,
                Region = CellRegion,
            },
            CancellationToken.None
        );

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
    public async Task PlaceAsync_WhenTheTenantIsNotReserved_RecordsThePlacement()
    {
        await RegisterCellAsync();

        await Tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = Issuer,
                Cell = CellName,
                Region = CellRegion,
            },
            CancellationToken.None
        );

        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(placement);
        Assert.Equal(Issuer, placement.Issuer);
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsRetired_KeepsItRetired()
    {
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            CancellationToken.None
        );

        await Tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = Issuer,
                Cell = CellName,
                Region = CellRegion,
            },
            CancellationToken.None
        );

        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
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
    public async Task ChangeIssuerAsync_WhenTheTenantIsUnknown_RetiresThePreviousIssuer()
    {
        var result = await ChangeIssuerAsync(TenantId, Issuer, FreeIssuer);

        Assert.True(result.IsReserved);
        Assert.Null(await ReadTenantAsync(TenantId));
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantIsRetired_KeepsTheRecordWithoutAnIssuer()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        var result = await ChangeIssuerAsync(TenantId, previousIssuer: null, FreeIssuer);

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateRetired, record.State);
        Assert.Null(record.Issuer);
    }

    #endregion
}
