// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreCellTests : DefaultDirectoryStoreTestBase
{
    private static CellDescriptor CreateCell(string name, string region = "region-a") =>
        new()
        {
            Name = name,
            Region = region,
            Kind = CellKinds.Shared,
            ManagementUrl = $"https://{name}.example/",
        };

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WhenTheCellIsNew_StoresTheDescriptor()
    {
        var descriptor = CreateCell("cell-a") with { IsControlPlane = true };

        await Cells.RegisterAsync(descriptor, CancellationToken.None);

        var stored = await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None);
        Assert.Equal(descriptor, stored);
    }

    [Fact]
    public async Task RegisterAsync_WhenTheCellIsRegisteredAgain_ReplacesTheDescriptor()
    {
        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);
        var changed = CreateCell("cell-a", region: "region-b") with
        {
            Kind = CellKinds.Dedicated,
            ManagementUrl = "https://moved.example/",
            IsControlPlane = true,
        };

        await Cells.RegisterAsync(changed, CancellationToken.None);

        Assert.Equal(changed, await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None));
        Assert.Single(await Cells.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_WhenAnotherCellReportsTheManagementUrl_ThrowsAndStoresNothing()
    {
        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);
        var other = CreateCell("cell-b") with { ManagementUrl = "https://cell-a.example/" };

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await Cells.RegisterAsync(other, CancellationToken.None)
        );

        Assert.Contains("cell-a", exception.Message);
        Assert.Null(await Cells.GetOrDefaultAsync("cell-b", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://CELL-A.example/")]
    [InlineData("https://cell-a.example")]
    [InlineData("https://cell-a.example:443/")]
    public async Task RegisterAsync_WhenTheManagementUrlDiffersOnlyInForm_Throws(string url)
    {
        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);
        var other = CreateCell("cell-b") with { ManagementUrl = url };

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await Cells.RegisterAsync(other, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RegisterAsync_WhenTheSameCellReportsItsUrlAgain_Accepts()
    {
        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);

        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);

        Assert.Single(await Cells.ListAsync(CancellationToken.None));
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenCellsAreRegistered_ReturnsThemByName()
    {
        await Cells.RegisterAsync(CreateCell("cell-b"), CancellationToken.None);
        await Cells.RegisterAsync(CreateCell("cell-a"), CancellationToken.None);

        var cells = await Cells.ListAsync(CancellationToken.None);

        Assert.Equal(["cell-a", "cell-b"], cells.Select(cell => cell.Name));
    }

    [Fact]
    public async Task ListAsync_WhenNoCellIsRegistered_ReturnsNothing()
    {
        var cells = await Cells.ListAsync(CancellationToken.None);

        Assert.Empty(cells);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheCellIsUnknown_ReturnsNull()
    {
        var cell = await Cells.GetOrDefaultAsync("unknown", CancellationToken.None);

        Assert.Null(cell);
    }

    #endregion
}
