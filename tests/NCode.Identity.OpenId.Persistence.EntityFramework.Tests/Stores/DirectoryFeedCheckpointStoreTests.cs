// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses SQLite because the EF InMemory provider does not enforce concurrency tokens, which keep a raise from a stale read
// from overwriting a higher checkpoint.
public sealed class DirectoryFeedCheckpointStoreTests : IDisposable
{
    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }

    public DirectoryFeedCheckpointStoreTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));
        Provider = serviceCollection.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OpenIdDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private static DirectoryFeedCheckpointStore CreateStore(OpenIdDbContext context) =>
        new(Mock.Of<IStoreProvider>(), context);

    private async ValueTask RecordAsync(long sequence, CancellationToken cancellationToken)
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        await CreateStore(context).RecordAppliedSequenceAsync(sequence, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask<long> GetAsync(CancellationToken cancellationToken)
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).GetAppliedSequenceAsync(cancellationToken);
    }

    private async ValueTask<DirectoryFeedCheckpointEntity> ReadRowAsync(
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await context.DirectoryFeedCheckpoints.AsNoTracking().SingleAsync(cancellationToken);
    }

    #endregion

    #region GetAppliedSequenceAsync Tests

    [Fact]
    public async Task GetAppliedSequenceAsync_BeforeAnyRecord_ReturnsZero()
    {
        var actual = await GetAsync(CancellationToken.None);

        Assert.Equal(0L, actual);
    }

    #endregion

    #region RecordAppliedSequenceAsync Tests

    [Fact]
    public async Task RecordAppliedSequenceAsync_OnFirstUse_CreatesTheCheckpoint()
    {
        const long sequence = 42;

        await RecordAsync(sequence, CancellationToken.None);

        var row = await ReadRowAsync(CancellationToken.None);
        Assert.Equal(DirectoryFeedCheckpointEntity.SingletonKey, row.Key);
        Assert.Equal(sequence, row.AppliedSequence);
        Assert.False(string.IsNullOrEmpty(row.ConcurrencyToken));
        Assert.Equal(sequence, await GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordAppliedSequenceAsync_WithAHigherSequence_RaisesIt()
    {
        const long first = 42;
        const long higher = 57;
        await RecordAsync(first, CancellationToken.None);
        var before = await ReadRowAsync(CancellationToken.None);

        await RecordAsync(higher, CancellationToken.None);

        var after = await ReadRowAsync(CancellationToken.None);
        Assert.Equal(higher, after.AppliedSequence);
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(higher, await GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordAppliedSequenceAsync_WithALowerSequence_KeepsTheHigher()
    {
        const long higher = 57;
        const long lower = 42;
        await RecordAsync(higher, CancellationToken.None);
        var before = await ReadRowAsync(CancellationToken.None);

        await RecordAsync(lower, CancellationToken.None);

        var after = await ReadRowAsync(CancellationToken.None);
        Assert.Equal(higher, after.AppliedSequence);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(higher, await GetAsync(CancellationToken.None));
    }

    #endregion
}
