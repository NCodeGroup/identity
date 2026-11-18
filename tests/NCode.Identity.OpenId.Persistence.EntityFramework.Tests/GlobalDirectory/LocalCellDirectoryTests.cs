// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public sealed class LocalCellDirectoryTests
{
    private static CellOptions CreateOptions() =>
        new()
        {
            Name = "cell-a",
            Region = "region-a",
            Kind = CellKinds.Dedicated,
            ManagementUrl = "https://cell-a.example/",
            IsControlPlane = true,
        };

    private static LocalCellDirectory CreateDirectory() => new(Options.Create(CreateOptions()));

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WhenCalledWithAnotherCell_ListsOnlyThisCell()
    {
        var directory = CreateDirectory();

        await directory.RegisterAsync(
            new CellDescriptor
            {
                Name = "cell-b",
                Region = "region-b",
                Kind = CellKinds.Shared,
                ManagementUrl = "https://cell-b.example/",
            },
            CancellationToken.None
        );

        var cell = Assert.Single(await directory.ListAsync(CancellationToken.None));
        Assert.Equal("cell-a", cell.Name);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenCalled_ReturnsTheCellTheOptionsDescribe()
    {
        var directory = CreateDirectory();

        var cell = Assert.Single(await directory.ListAsync(CancellationToken.None));

        Assert.Equal("cell-a", cell.Name);
        Assert.Equal("region-a", cell.Region);
        Assert.Equal(CellKinds.Dedicated, cell.Kind);
        Assert.Equal("https://cell-a.example/", cell.ManagementUrl);
        Assert.True(cell.IsControlPlane);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheNameIsThisCell_ReturnsItsDescriptor()
    {
        var directory = CreateDirectory();

        var cell = await directory.GetOrDefaultAsync("CELL-A", CancellationToken.None);

        Assert.NotNull(cell);
        Assert.Equal("cell-a", cell.Name);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheNameIsAnotherCell_ReturnsNull()
    {
        var directory = CreateDirectory();

        var cell = await directory.GetOrDefaultAsync("cell-b", CancellationToken.None);

        Assert.Null(cell);
    }

    #endregion
}
