// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreReserveTests : DefaultDirectoryStoreTestBase
{
    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierAndIssuerAreFree_ReservesTheTenant()
    {
        var result = await ReserveAsync(TenantId, Issuer, teamId: "team-1");

        Assert.True(result.IsReserved);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateReserved, record.State);
        Assert.Equal(Issuer, record.Issuer);
        Assert.Equal(Issuer, record.NormalizedIssuer);
        Assert.Equal("team-1", record.NormalizedTeamId);
        Assert.Equal(CellName, record.Cell);
        Assert.Equal(TimeProvider.GetUtcNow(), record.CreatedWhen);
        Assert.Equal(TimeProvider.GetUtcNow(), record.ModifiedWhen);
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsRetired_ConflictsWithTheDeletedTenantText()
    {
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId },
            CancellationToken.None
        );

        var result = await ReserveAsync("TENANT-1", FreeIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The tenant identifier 'TENANT-1' belonged to a deleted tenant and is never reissued.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsHeld_ConflictsWithTheHeldIdentifierText()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await ReserveAsync("Tenant-1", FreeIssuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            "Another tenant already has the identifier 'Tenant-1'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIssuerIsHeld_ConflictsWithTheHeldIssuerText()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await ReserveAsync(OtherTenantId, Issuer.ToUpperInvariant());

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the issuer '{Issuer.ToUpperInvariant()}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherTenantRetiredTheIssuer_ConflictsWithTheRetiredIssuerText()
    {
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
    public async Task ReserveAsync_WhenTheTeamIsBelowItsLimit_ReservesTheTenant()
    {
        await PublishTeamAsync("team-1", maxTenants: 2);
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIsAtItsLimit_ConflictsWithTheTeamLimitText()
    {
        await PublishTeamAsync("Team-1", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'Team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenAPlacedTenantFillsTheTeam_ConflictsWithTheTeamLimitText()
    {
        await PublishTeamAsync("team-1", maxTenants: 1);
        await ReserveAndPlaceAsync(TenantId, Issuer, "team-1");

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheParentTeamIsAtItsLimit_ConflictsWithTheParentTeamText()
    {
        await PublishTeamAsync("parent", maxTenants: 1);
        await PublishTeamAsync("child", parentTeamId: "parent");
        Assert.True((await ReserveAsync(TenantId, Issuer, "child")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "child");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'parent' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheParentTeamHoldsTenantsOfASiblingTeam_CountsThemAgainstTheParent()
    {
        await PublishTeamAsync("parent", maxTenants: 1);
        await PublishTeamAsync("child-a", parentTeamId: "parent");
        await PublishTeamAsync("child-b", parentTeamId: "parent");
        Assert.True((await ReserveAsync(TenantId, Issuer, "child-a")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "child-b");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'parent' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheGrandparentTeamIsAtItsLimit_CountsTheWholeSubtree()
    {
        await PublishTeamAsync("grandparent", maxTenants: 1);
        await PublishTeamAsync("parent", parentTeamId: "grandparent");
        await PublishTeamAsync("child", parentTeamId: "parent");
        Assert.True((await ReserveAsync(TenantId, Issuer, "parent")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "child");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'grandparent' holds its maximum of 1 tenants.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheChildAndTheParentAreAtTheirLimits_ReportsTheDeepestTeam()
    {
        await PublishTeamAsync("parent", maxTenants: 1);
        await PublishTeamAsync("child", parentTeamId: "parent", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "child")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "child");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'child' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenAChildTeamIsBelowItsLimit_DoesNotCountTenantsOfItsParent()
    {
        await PublishTeamAsync("parent");
        await PublishTeamAsync("child", parentTeamId: "parent", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "parent")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "child");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformIsAtItsLimit_ConflictsWithTheTeamLimitText()
    {
        await PublishPlatformAsync(maxTenants: 1);
        await PublishTeamAsync("team-1");
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformLimitIsLowerThanTheTeamLimit_UsesThePlatformLimit()
    {
        await PublishPlatformAsync(maxTenants: 1);
        await PublishTeamAsync("team-1", maxTenants: 5);
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamLimitIsLowerThanThePlatformLimit_UsesTheTeamLimit()
    {
        await PublishPlatformAsync(maxTenants: 5);
        await PublishTeamAsync("team-1", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenNoLimitIsSet_ReservesWithoutLimit()
    {
        await PublishPlatformAsync(maxTenants: null);
        await PublishTeamAsync("team-1");
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheLimitIsFilledByARetiredTenant_ReservesTheTenant()
    {
        await PublishTeamAsync("team-1", maxTenants: 1);
        await ReserveAndPlaceAsync(TenantId, Issuer, "team-1");
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenAReservationIsReleased_FreesTheLimit()
    {
        await PublishTeamAsync("team-1", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);
        await Tenants.ReleaseAsync(TenantId, CancellationToken.None);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIsUnknown_ReservesWithoutLimit()
    {
        await PublishPlatformAsync(maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, "unknown")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "unknown");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamsFormACycle_StopsWalkingTheLineage()
    {
        await PublishTeamAsync("team-a", parentTeamId: "team-b", maxTenants: 5);
        await PublishTeamAsync("team-b", parentTeamId: "team-a", maxTenants: 5);

        var result = await ReserveAsync(TenantId, Issuer, "team-a");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformDocumentHasNoLimit_ReservesWithoutLimit()
    {
        await Platform.PublishAsync(
            new PlatformDocument { SettingsJson = "not json" },
            CancellationToken.None
        );
        await PublishTeamAsync("team-1");
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    #endregion
}
