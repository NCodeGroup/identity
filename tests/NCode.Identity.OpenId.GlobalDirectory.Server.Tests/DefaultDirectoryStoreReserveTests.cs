// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreReserveTests : DefaultDirectoryStoreTestBase
{
    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierAndIssuerAreFree_ReservesTheTenant()
    {
        await PublishTeamAsync("team-1");

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
    public async Task ReserveAsync_WhenTheReservationIsRetriedAfterItCommitted_IsReserved()
    {
        await PublishTeamAsync("team-1", maxTenants: 1);
        Assert.True((await ReserveAsync(TenantId, Issuer, teamId: "team-1")).IsReserved);

        var result = await ReserveAsync(TenantId, Issuer, teamId: "team-1");

        Assert.True(result.IsReserved);
        Assert.Null(result.ConflictDetail);
        var record = await ReadTenantAsync(TenantId);
        Assert.NotNull(record);
        Assert.Equal(TenantRecord.StateReserved, record.State);
        Assert.Equal(CellName, record.Cell);
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherCellHoldsTheReservation_ConflictsWithTheHeldIdentifierText()
    {
        Assert.True((await ReserveAsync(TenantId, Issuer)).IsReserved);

        var result = await Tenants.ReserveAsync(
            new TenantReservation
            {
                TenantId = TenantId,
                Issuer = Issuer,
                Cell = "cell-b",
            },
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTenantIsPlaced_ConflictsWithTheHeldIdentifierText()
    {
        await ReserveAndPlaceAsync(TenantId, Issuer);

        var result = await ReserveAsync(TenantId, Issuer);

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheReservationNamesAnotherTeam_ConflictsWithTheHeldIdentifierText()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");
        Assert.True((await ReserveAsync(TenantId, Issuer, teamId: "team-1")).IsReserved);

        var result = await ReserveAsync(TenantId, Issuer, teamId: "team-2");

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
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
    public async Task ReserveAsync_WhenAGrandchildTeamHoldsTheLimitOfItsGrandparent_CountsItByTheStoredPath()
    {
        await PublishTeamAsync("grandparent", maxTenants: 1);
        await PublishTeamAsync("parent", parentTeamId: "grandparent");
        await PublishTeamAsync("child", parentTeamId: "parent");
        Assert.True((await ReserveAsync(TenantId, Issuer, "child")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "grandparent");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'grandparent' holds its maximum of 1 tenants.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenAnUnrelatedTeamSharesAPathPrefixByName_DoesNotCountItsTenants()
    {
        await PublishTeamAsync("acme", maxTenants: 1);
        await PublishTeamAsync("acme-web");
        Assert.True((await ReserveAsync(TenantId, Issuer, "acme-web")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "acme");

        Assert.True(result.IsReserved);
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
    public async Task ReserveAsync_WhenTheTeamIsUnknown_Conflicts()
    {
        await PublishPlatformAsync(maxTenants: 1);

        var result = await ReserveAsync(TenantId, Issuer, "unknown");

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'unknown' has no record.", result.ConflictDetail);
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
            new PlatformDocument { Settings = JsonElement.Parse("{}") },
            CancellationToken.None
        );
        await PublishTeamAsync("team-1");
        Assert.True((await ReserveAsync(TenantId, Issuer, "team-1")).IsReserved);

        var result = await ReserveAsync(OtherTenantId, OtherIssuer, "team-1");

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamLimitIsUnreadable_ReturnsConflict()
    {
        await Teams.PublishAsync(
            new TeamDocument
            {
                TeamId = "team-1",
                DisplayName = "team-1",
                IsDisabled = false,
                Settings = JsonElement.Parse("""{"max_tenants":"3"}"""),
                RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            },
            CancellationToken.None
        );

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'team-1' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformLimitIsNegative_ReturnsConflict()
    {
        await Platform.PublishAsync(
            new PlatformDocument { Settings = JsonElement.Parse("""{"max_tenants":-1}""") },
            CancellationToken.None
        );
        await PublishTeamAsync("team-1");

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamSettingsAreNotAnObject_ReturnsConflict()
    {
        await Teams.PublishAsync(
            new TeamDocument
            {
                TeamId = "team-1",
                DisplayName = "team-1",
                IsDisabled = false,
                Settings = JsonElement.Parse("[1]"),
                RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            },
            CancellationToken.None
        );

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'team-1' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformSettingsAreNotAnObject_ReturnsConflict()
    {
        await Platform.PublishAsync(
            new PlatformDocument { Settings = JsonElement.Parse("[1]") },
            CancellationToken.None
        );
        await PublishTeamAsync("team-1");

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamSettingsAreNull_ReturnsConflict()
    {
        await Teams.PublishAsync(
            new TeamDocument
            {
                TeamId = "team-1",
                DisplayName = "team-1",
                IsDisabled = false,
                Settings = JsonElement.Parse("null"),
                RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            },
            CancellationToken.None
        );

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'team-1' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"teamId":"team-1","displayName":"team-1","isDisabled":false}""")]
    public async Task ReserveAsync_WhenTheStoredTeamDocumentIsNotAnEnvelope_ReturnsConflict(
        string documentJson
    )
    {
        await PublishTeamAsync("team-1");
        await ReplaceTeamDocumentJsonAsync("team-1", documentJson);

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'team-1' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"settings":{}}""")]
    public async Task ReserveAsync_WhenTheStoredPlatformDocumentIsNotAnEnvelope_ReturnsConflict(
        string documentJson
    )
    {
        await PublishPlatformAsync(maxTenants: null);
        await PublishTeamAsync("team-1");
        await ReplacePlatformDocumentJsonAsync(documentJson);

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformSettingsAreNull_ReturnsConflict()
    {
        await Platform.PublishAsync(
            new PlatformDocument { Settings = JsonElement.Parse("null") },
            CancellationToken.None
        );
        await PublishTeamAsync("team-1");

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheStoredTeamDocumentLacksItsSettings_ReturnsConflict()
    {
        await PublishTeamAsync("team-1");
        await ReplaceTeamDocumentJsonAsync(
            "team-1",
            """{"$ver":1,"$payload":{"teamId":"team-1","displayName":"team-1","isDisabled":false,"rootKeys":{}}}"""
        );

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'team-1' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheStoredPlatformDocumentLacksItsSettings_ReturnsConflict()
    {
        await PublishPlatformAsync(maxTenants: null);
        await PublishTeamAsync("team-1");
        await ReplacePlatformDocumentJsonAsync("""{"$ver":1,"$payload":{"sequence":0}}""");

        var result = await ReserveAsync(TenantId, Issuer, "team-1");

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    #endregion
}
