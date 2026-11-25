// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreCellRegistryTests : DefaultDirectoryStoreTestBase
{
    private static CellRegistration CreateRegistration(
        string name,
        string region = "region-a",
        string kind = CellKinds.Shared,
        string jwks = """{"keys":[{"kid":"one"}]}"""
    ) =>
        new()
        {
            Name = name,
            Region = region,
            Kind = kind,
            Jwks = JsonElement.Parse(jwks),
        };

    private static CellDescriptor CreateDescriptor(string name, string region = "region-a") =>
        new()
        {
            Name = name,
            Region = region,
            Kind = CellKinds.Shared,
            ManagementUrl = $"https://{name}.example/",
        };

    #region RegisterCellAsync Tests

    [Fact]
    public async Task RegisterCellAsync_WhenTheCellIsNew_StoresTheKeysAndListsTheCellWithoutAManagementUrl()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);

        var keys = await CellRegistry.GetCellClientKeysAsync("cell-a", CancellationToken.None);
        Assert.NotNull(keys);
        Assert.Equal("one", keys.Value.GetProperty("keys")[0].GetProperty("kid").GetString());
        var listed = await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None);
        Assert.NotNull(listed);
        Assert.True(listed.IsRegistered);
        Assert.Null(listed.ManagementUrl);
        Assert.Single(await Cells.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheCellIsRegisteredAgain_ReplacesTheKeys()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);

        await CellRegistry.RegisterCellAsync(
            CreateRegistration("cell-a", jwks: """{"keys":[{"kid":"two"}]}"""),
            CancellationToken.None
        );

        var keys = await CellRegistry.GetCellClientKeysAsync("cell-a", CancellationToken.None);
        Assert.NotNull(keys);
        Assert.Equal("two", keys.Value.GetProperty("keys")[0].GetProperty("kid").GetString());
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheCellIsRegisteredAgain_KeepsTheReportedDescriptor()
    {
        await ReportCellAsync(CreateDescriptor("cell-a"), CancellationToken.None);

        await CellRegistry.RegisterCellAsync(
            CreateRegistration("cell-a", jwks: """{"keys":[]}"""),
            CancellationToken.None
        );

        Assert.NotNull(await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None));
    }

    [Theory]
    [InlineData("region-b", CellKinds.Shared)]
    [InlineData("region-a", CellKinds.Dedicated)]
    public async Task RegisterCellAsync_WhenTheRegionOrKindChanges_Throws(
        string region,
        string kind
    )
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RegisterCellAsync(
                CreateRegistration("cell-a", region, kind),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheNameIsTheControlPlaneCell_Throws()
    {
        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RegisterCellAsync(
                CreateRegistration(ControlPlaneCell),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheCellIsRetired_Throws()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);
        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RegisterCellAsync(
                CreateRegistration("cell-a"),
                CancellationToken.None
            )
        );
    }

    #endregion

    #region RetireCellAsync Tests

    [Fact]
    public async Task RetireCellAsync_WhenTheCellHoldsNoTenant_RetiresItAndDropsItsKeysAndKeepsItListed()
    {
        await ReportCellAsync(CreateDescriptor("cell-a"), CancellationToken.None);

        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);

        Assert.Null(await CellRegistry.GetCellClientKeysAsync("cell-a", CancellationToken.None));
        var retired = await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None);
        Assert.NotNull(retired);
        Assert.True(retired.IsRetired);
        Assert.Null(retired.ManagementUrl);
        Assert.Single(await Cells.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheCellIsRetiredAlready_Accepts()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);
        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);

        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);

        Assert.Null(await CellRegistry.GetCellClientKeysAsync("cell-a", CancellationToken.None));
    }

    [Fact]
    public async Task RetireCellAsync_WhenATenantIsPlacedInTheCell_Throws()
    {
        await ReportCellAsync(CreateDescriptor(CellName), CancellationToken.None);
        await ReserveAndPlaceAsync(TenantId, Issuer);

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RetireCellAsync(CellName, CancellationToken.None)
        );

        Assert.NotNull(await CellRegistry.GetCellClientKeysAsync(CellName, CancellationToken.None));
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheCellsTenantsAreRetired_RetiresIt()
    {
        await ReportCellAsync(CreateDescriptor(CellName), CancellationToken.None);
        await ReserveAndPlaceAsync(TenantId, Issuer);
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = Issuer },
            CancellationToken.None
        );

        await CellRegistry.RetireCellAsync(CellName, CancellationToken.None);

        Assert.Null(await CellRegistry.GetCellClientKeysAsync(CellName, CancellationToken.None));
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheCellIsUnknown_Throws()
    {
        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RetireCellAsync("unknown", CancellationToken.None)
        );
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheNameIsTheControlPlaneCell_Throws()
    {
        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await CellRegistry.RetireCellAsync(ControlPlaneCell, CancellationToken.None)
        );
    }

    #endregion

    #region Descriptor Report Tests

    [Fact]
    public async Task ReportAsync_WhenTheCellIsNotRegistered_Throws()
    {
        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await Cells.ReportAsync(CreateDescriptor("cell-a"), CancellationToken.None)
        );

        Assert.Null(await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None));
    }

    [Fact]
    public async Task ReportAsync_WhenTheCellIsRegistered_StoresTheDescriptor()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);
        var descriptor = CreateDescriptor("cell-a");

        await Cells.ReportAsync(descriptor, CancellationToken.None);

        Assert.Equal(
            descriptor with
            {
                IsRegistered = true,
            },
            await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None)
        );
    }

    [Fact]
    public async Task ReportAsync_WhenTheCellReportsItselfAsTheControlPlane_KeepsTheStoredFlag()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);

        await Cells.ReportAsync(
            CreateDescriptor("cell-a") with
            {
                IsControlPlane = true,
            },
            CancellationToken.None
        );

        var stored = await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.False(stored.IsControlPlane);
    }

    [Theory]
    [InlineData("region-b", CellKinds.Shared)]
    [InlineData("region-a", CellKinds.Dedicated)]
    public async Task ReportAsync_WhenTheRegionOrKindDiffersFromTheRegistration_Throws(
        string region,
        string kind
    )
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await Cells.ReportAsync(
                CreateDescriptor("cell-a", region) with
                {
                    Kind = kind,
                },
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ReportAsync_WhenTheCellIsRetired_Throws()
    {
        await CellRegistry.RegisterCellAsync(CreateRegistration("cell-a"), CancellationToken.None);
        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await Cells.ReportAsync(CreateDescriptor("cell-a"), CancellationToken.None)
        );
    }

    #endregion

    #region GetCellClientKeysAsync Tests

    [Fact]
    public async Task GetCellClientKeysAsync_WhenTheCellIsUnknown_ReturnsNull()
    {
        var keys = await CellRegistry.GetCellClientKeysAsync("unknown", CancellationToken.None);

        Assert.Null(keys);
    }

    #endregion
}
