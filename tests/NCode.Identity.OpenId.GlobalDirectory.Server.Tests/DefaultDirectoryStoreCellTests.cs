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

    #region ReportAsync Tests

    [Fact]
    public async Task ReportAsync_WhenTheCellIsNew_StoresTheDescriptor()
    {
        var descriptor = CreateCell("cell-a");

        await ReportCellAsync(descriptor, CancellationToken.None);

        var stored = await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None);
        Assert.Equal(descriptor with { IsRegistered = true }, stored);
    }

    [Fact]
    public async Task ReportAsync_WhenTheCellIsRegisteredAgain_ReplacesTheDescriptor()
    {
        await ReportCellAsync(CreateCell("cell-a"), CancellationToken.None);
        var changed = CreateCell("cell-a") with { ManagementUrl = "https://moved.example/" };

        await Cells.ReportAsync(changed, CancellationToken.None);

        Assert.Equal(
            changed with
            {
                IsRegistered = true,
            },
            await Cells.GetOrDefaultAsync("cell-a", CancellationToken.None)
        );
        var listedCells = await Cells.ListAsync(CancellationToken.None);
        Assert.Equal(["cell-a", ControlPlaneCell], listedCells.Select(cell => cell.Name));
    }

    [Fact]
    public async Task ReportAsync_WhenAnotherCellReportsTheManagementUrl_ThrowsAndStoresNothing()
    {
        await ReportCellAsync(CreateCell("cell-a"), CancellationToken.None);
        var other = CreateCell("cell-b") with { ManagementUrl = "https://cell-a.example/" };

        var exception = await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await ReportCellAsync(other, CancellationToken.None)
        );

        Assert.Contains("cell-a", exception.Message);
        var stored = await Cells.GetOrDefaultAsync("cell-b", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Null(stored.ManagementUrl);
    }

    [Theory]
    [InlineData("https://CELL-A.example/")]
    [InlineData("https://cell-a.example")]
    [InlineData("https://cell-a.example:443/")]
    public async Task ReportAsync_WhenTheManagementUrlDiffersOnlyInForm_Throws(string url)
    {
        await ReportCellAsync(CreateCell("cell-a"), CancellationToken.None);
        var other = CreateCell("cell-b") with { ManagementUrl = url };

        await Assert.ThrowsAsync<CellDirectoryConflictException>(async () =>
            await ReportCellAsync(other, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ReportAsync_WhenTheSameCellReportsItsUrlAgain_Accepts()
    {
        await ReportCellAsync(CreateCell("cell-a"), CancellationToken.None);

        await Cells.ReportAsync(CreateCell("cell-a"), CancellationToken.None);

        var listedCells = await Cells.ListAsync(CancellationToken.None);
        Assert.Equal(["cell-a", ControlPlaneCell], listedCells.Select(cell => cell.Name));
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenCellsAreRegistered_ReturnsThemByName()
    {
        await ReportCellAsync(CreateCell("cell-b"), CancellationToken.None);
        await ReportCellAsync(CreateCell("cell-a"), CancellationToken.None);

        var cells = await Cells.ListAsync(CancellationToken.None);

        Assert.Equal(["cell-a", "cell-b", ControlPlaneCell], cells.Select(cell => cell.Name));
    }

    [Fact]
    public async Task ListAsync_WhenNoCellIsRegistered_ReturnsOnlyTheControlPlaneCell()
    {
        var cells = await Cells.ListAsync(CancellationToken.None);

        Assert.Equal([ControlPlaneCell], cells.Select(cell => cell.Name));
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
