// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses SQLite because the EF InMemory provider does not enforce concurrency tokens.
public sealed class ReplicatedDocumentStoreTests : IDisposable
{
    private const string Kind = "platform-authority";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }

    public ReplicatedDocumentStoreTests()
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

    private static ReplicatedDocumentStore CreateStore(OpenIdDbContext context) =>
        new(Mock.Of<IStoreProvider>(), context);

    private static PersistedReplicatedDocument CreateDocument(long revision, string? content) =>
        new()
        {
            Kind = Kind,
            Key = string.Empty,
            Revision = revision,
            ContentJson = content,
        };

    private async ValueTask<bool> ApplyAsync(
        PersistedReplicatedDocument document,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var applied = await CreateStore(context).TryApplyAsync(document, cancellationToken);
        await context.SaveChangesAsync(CancellationToken.None);
        return applied;
    }

    private async ValueTask<PersistedReplicatedDocument?> GetAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).GetOrDefaultAsync(Kind, string.Empty, cancellationToken);
    }

    #endregion

    [Fact]
    public async Task GetOrDefaultAsync_WithNoReplica_ReturnsNull()
    {
        Assert.Null(await GetAsync());
    }

    [Fact]
    public async Task TryApplyAsync_OnFirstUse_StoresTheDocument()
    {
        var applied = await ApplyAsync(CreateDocument(3, "{\"a\":1}"));

        Assert.True(applied);
        var actual = await GetAsync();
        Assert.NotNull(actual);
        Assert.Equal(3, actual.Revision);
        Assert.Equal("{\"a\":1}", actual.ContentJson);
    }

    [Fact]
    public async Task TryApplyAsync_WithAHigherRevision_ReplacesTheDocument()
    {
        await ApplyAsync(CreateDocument(3, "{\"a\":1}"));

        var applied = await ApplyAsync(CreateDocument(4, "{\"a\":2}"));

        Assert.True(applied);
        var actual = await GetAsync();
        Assert.Equal(4, actual!.Revision);
        Assert.Equal("{\"a\":2}", actual.ContentJson);
    }

    [Fact]
    public async Task TryApplyAsync_WithALowerRevision_KeepsTheDocument()
    {
        await ApplyAsync(CreateDocument(4, "{\"a\":2}"));

        var applied = await ApplyAsync(CreateDocument(3, "{\"a\":1}"));

        Assert.False(applied);
        var actual = await GetAsync();
        Assert.Equal(4, actual!.Revision);
        Assert.Equal("{\"a\":2}", actual.ContentJson);
    }

    [Fact]
    public async Task TryApplyAsync_WithTheSameRevisionAndContent_KeepsTheDocument()
    {
        await ApplyAsync(CreateDocument(4, "{\"a\":2}"));

        var applied = await ApplyAsync(CreateDocument(4, "{\"a\":3}"));

        Assert.False(applied);
        Assert.Equal("{\"a\":2}", (await GetAsync())!.ContentJson);
    }

    [Fact]
    public async Task TryApplyAsync_WithTheSameRevisionAndNoContent_RetiresTheDocument()
    {
        await ApplyAsync(CreateDocument(4, "{\"a\":2}"));

        var applied = await ApplyAsync(CreateDocument(4, null));

        Assert.True(applied);
        var actual = await GetAsync();
        Assert.Equal(4, actual!.Revision);
        Assert.Null(actual.ContentJson);
    }

    [Fact]
    public async Task TryApplyAsync_WithTheSameRevisionAndNoContentOnARetiredDocument_ChangesNothing()
    {
        await ApplyAsync(CreateDocument(4, null));

        var applied = await ApplyAsync(CreateDocument(4, null));

        Assert.False(applied);
    }

    [Fact]
    public async Task ListByKindAsync_ReturnsOnlyThatKind()
    {
        await ApplyAsync(CreateDocument(1, "{}"));
        await ApplyAsync(
            new PersistedReplicatedDocument
            {
                Kind = "management-keys",
                Key = string.Empty,
                Revision = 1,
                ContentJson = "{}",
            }
        );

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(context).ListByKindAsync(Kind, CancellationToken.None);

        var single = Assert.Single(actual);
        Assert.Equal(Kind, single.Kind);
    }

    [Fact]
    public async Task AddRevokedKeyAsync_RecordsTheKeyOnceAndListsIt()
    {
        var first = new PersistedRevokedManagementKey
        {
            KeyId = "key-1",
            RevokedWhen = DateTimeOffset.UtcNow,
            Source = PersistedRevokedManagementKey.ControlPlaneSource,
        };
        var second = new PersistedRevokedManagementKey
        {
            KeyId = "key-1",
            RevokedWhen = DateTimeOffset.UtcNow.AddHours(1),
            Source = PersistedRevokedManagementKey.OfflineSource,
        };

        foreach (var key in new[] { first, second })
        {
            using var scope = Provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
            await CreateStore(context).AddRevokedKeyAsync(key, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var readScope = Provider.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var actual = await CreateStore(readContext).ListRevokedKeysAsync(CancellationToken.None);

        var single = Assert.Single(actual);
        Assert.Equal("key-1", single.KeyId);
        Assert.Equal(PersistedRevokedManagementKey.ControlPlaneSource, single.Source);
    }
}
