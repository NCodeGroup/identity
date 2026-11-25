// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DirectoryDbContextCellIndexTests : IDisposable
{
    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    public DirectoryDbContextCellIndexTests()
    {
        Connection.Open();
        using var dbContext = CreateDbContext();
        dbContext.Database.EnsureCreated();
    }

    public void Dispose() => Connection.Dispose();

    private DirectoryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DirectoryDbContext>().UseSqlite(Connection).Options);

    private static CellRecord CreateCell(string name, string? normalizedUrl) =>
        new()
        {
            Name = name,
            Region = "region-a",
            Kind = CellKinds.Shared,
            ManagementUrl = normalizedUrl,
            NormalizedManagementUrl = normalizedUrl,
        };

    [Fact]
    public async Task SaveChangesAsync_WhenTwoCellsShareANormalizedManagementUrl_Throws()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Cells.Add(CreateCell("cell-a", "https://cell.example"));
        dbContext.Cells.Add(CreateCell("cell-b", "https://cell.example"));

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await dbContext.SaveChangesAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCellsHaveNoManagementUrl_Accepts()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Cells.Add(CreateCell("cell-a", null));
        dbContext.Cells.Add(CreateCell("cell-b", null));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
