// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses SQLite because the EF InMemory provider does not enforce concurrency tokens or generate identities as a relational
// database does.
public sealed class DirectoryOutboxStoreTests : IDisposable
{
    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }

    public DirectoryOutboxStoreTests()
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

    private static DirectoryOutboxStore CreateStore(OpenIdDbContext context) =>
        new(Mock.Of<IStoreProvider>(), context);

    private async ValueTask AddOutboxAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        context
            .Set<DirectoryOutboxEntity>()
            .Add(
                new DirectoryOutboxEntity
                {
                    Kind = kind,
                    Key = key,
                    Revision = revision,
                    ContentJson = "{}",
                    CreatedWhen = DateTimeOffset.UtcNow,
                }
            );
        await context.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask AddSourceRevisionAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        context
            .Set<ReplicatedSourceRevisionEntity>()
            .Add(
                new ReplicatedSourceRevisionEntity
                {
                    Kind = kind,
                    Key = key,
                    Revision = revision,
                    ConcurrencyToken = Guid.NewGuid().ToString("N"),
                }
            );
        await context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    [Fact]
    public async Task ListPendingAsync_OrdersByKindKeyAndRevision()
    {
        await AddOutboxAsync("team-authority", "b", 2);
        await AddOutboxAsync("platform-authority", "", 5);
        await AddOutboxAsync("team-authority", "b", 1);
        await AddOutboxAsync("team-authority", "a", 9);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(context).ListPendingAsync(10, CancellationToken.None);

        Assert.Equal(
            [
                "platform-authority::5",
                "team-authority:a:9",
                "team-authority:b:1",
                "team-authority:b:2",
            ],
            actual.Select(entry => $"{entry.Kind}:{entry.Key}:{entry.Revision}")
        );
        Assert.All(actual, entry => Assert.True(entry.Sequence > 0));
    }

    [Fact]
    public async Task ListPendingAsync_HonorsTheLimit()
    {
        await AddOutboxAsync("platform-authority", "", 1);
        await AddOutboxAsync("platform-authority", "", 2);
        await AddOutboxAsync("platform-authority", "", 3);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(context).ListPendingAsync(2, CancellationToken.None);

        Assert.Equal([1L, 2L], actual.Select(entry => entry.Revision));
    }

    [Fact]
    public async Task RemoveThroughAsync_RemovesOnlyTheDocumentsEntriesUpToTheRevision()
    {
        await AddOutboxAsync("platform-authority", "", 1);
        await AddOutboxAsync("platform-authority", "", 2);
        await AddOutboxAsync("platform-authority", "", 3);
        await AddOutboxAsync("management-keys", "", 1);

        using (var scope = Provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
            await CreateStore(context)
                .RemoveThroughAsync("platform-authority", "", 2, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var readScope = Provider.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(readContext).ListPendingAsync(10, CancellationToken.None);

        Assert.Equal(
            ["management-keys:1", "platform-authority:3"],
            actual.Select(entry => $"{entry.Kind}:{entry.Revision}")
        );
    }

    [Fact]
    public async Task ListSourceRevisionsAsync_ReturnsEveryDocumentsRevision()
    {
        await AddSourceRevisionAsync("team-authority", "t1", 4);
        await AddSourceRevisionAsync("platform-authority", "", 7);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(context).ListSourceRevisionsAsync(CancellationToken.None);

        Assert.Equal(
            ["platform-authority::7", "team-authority:t1:4"],
            actual.Select(entry => $"{entry.Kind}:{entry.Key}:{entry.Revision}")
        );
    }
}
