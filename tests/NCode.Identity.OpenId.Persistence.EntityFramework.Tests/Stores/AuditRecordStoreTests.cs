// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Adds through SQLite, because the EF InMemory provider does not enforce the unique event identifier or the foreign key
// that ties a resource to its record, and removes through the InMemory provider, because EF's SQLite provider cannot
// compare a DateTimeOffset in a query; the host's Npgsql provider does both (ADR-1303).
public sealed class AuditRecordStoreTests : IDisposable
{
    private static DateTimeOffset Now { get; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private FakeTimeProvider TimeProvider { get; } = new(Now);

    public AuditRecordStoreTests()
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

    private AuditRecordStore CreateStore(OpenIdDbContext context) =>
        new(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            context,
            TimeProvider
        );

    private static PersistedAuditRecord Record(
        Guid? eventId = null,
        DateTimeOffset? eventTime = null,
        string tenantId = "tenant-1",
        params PersistedAuditResource[] resources
    ) =>
        new()
        {
            EventId = eventId ?? Guid.CreateVersion7(),
            EventTime = eventTime ?? Now,
            EventName = "client.updated",
            EventCategory = "management",
            Outcome = "success",
            Severity = "info",
            Cell = "cell-1",
            TenantId = tenantId,
            ClientId = "client-1",
            ActorType = "user",
            ActorId = "admin-1",
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SourceIp = "203.0.113.7",
            Resources = resources,
            Record = JsonElement.Parse("""{"schema_version":"1.0"}"""),
        };

    private static ServiceProvider CreateInMemoryProvider()
    {
        // The configuration runs for each context, so every context of the provider must name the same database.
        var databaseName = Guid.NewGuid().ToString("N");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        return serviceCollection.BuildServiceProvider();
    }

    private ValueTask<int> AddAsync(
        PersistedAuditRecord[] records,
        CancellationToken cancellationToken
    ) => AddAsync(Provider, records, cancellationToken);

    private async ValueTask<int> AddAsync(
        ServiceProvider provider,
        PersistedAuditRecord[] records,
        CancellationToken cancellationToken
    )
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var added = await CreateStore(context).AddAsync(records, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async ValueTask<int> RemoveBeforeAsync(
        ServiceProvider provider,
        DateTimeOffset time,
        int limit,
        CancellationToken cancellationToken
    )
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var removed = await CreateStore(context).RemoveBeforeAsync(time, limit, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return removed;
    }

    private ValueTask<(int Records, int Resources)> CountAsync(
        CancellationToken cancellationToken
    ) => CountAsync(Provider, cancellationToken);

    private static async ValueTask<(int Records, int Resources)> CountAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken
    )
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var records = await context.AuditRecords.CountAsync(cancellationToken);
        var resources = await context.AuditRecordResources.CountAsync(cancellationToken);
        return (records, resources);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithAResourcefulRecord_KeepsItsEnvelopeAndResourcesInOrder()
    {
        var record = Record(
            resources:
            [
                new PersistedAuditResource { Type = "client", Id = "client-1" },
                new PersistedAuditResource { Type = "tenant", Id = "tenant-1" },
            ]
        );

        var added = await AddAsync([record], CancellationToken.None);

        Assert.Equal(1, added);
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var entity = await context
            .AuditRecords.Include(e => e.Resources)
            .SingleAsync(e => e.EventId == record.EventId);
        Assert.Equal(Now, entity.EventTime);
        Assert.Equal("client.updated", entity.EventName);
        Assert.Equal("tenant-1", entity.TenantId);
        Assert.Equal("admin-1", entity.ActorId);
        Assert.Equal("203.0.113.7", entity.SourceIp);
        Assert.Equal("cell-1", entity.Cell);
        Assert.True(JsonElement.DeepEquals(record.Record, entity.Record));
        Assert.Equal(
            [("client", "client-1", 0), ("tenant", "tenant-1", 1)],
            entity.Resources.OrderBy(r => r.Ordinal).Select(r => (r.Type, r.ResourceId!, r.Ordinal))
        );
    }

    [Fact]
    public async Task AddAsync_WhenTheStoreHoldsAnEvent_AddsOnlyTheOthers()
    {
        var held = Record();
        await AddAsync([held], CancellationToken.None);

        var added = await AddAsync([held, Record()], CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal((2, 0), await CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenABatchHoldsAnEventTwice_AddsItOnce()
    {
        var eventId = Guid.CreateVersion7();

        var added = await AddAsync([Record(eventId), Record(eventId)], CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal((1, 0), await CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenEmpty_Throws()
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await CreateStore(context).AddAsync([], CancellationToken.None)
        );
    }

    [Fact]
    public async Task AddAsync_Always_StampsTheStoredTime()
    {
        TimeProvider.SetUtcNow(Now.AddMinutes(5));
        var first = Record(eventTime: Now.AddDays(-1));
        var second = Record(eventTime: Now.AddDays(-2));

        await AddAsync([first, second], CancellationToken.None);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var storedTimes = await context.AuditRecords.Select(e => e.StoredTime).ToListAsync();
        Assert.Equal([Now.AddMinutes(5), Now.AddMinutes(5)], storedTimes);
    }

    #endregion

    #region GetStreamPageAsync Tests

    private async ValueTask<AuditRecordStreamPage> GetStreamPageAsync(
        ServiceProvider provider,
        AuditRecordStreamRequest request,
        CancellationToken cancellationToken
    )
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).GetStreamPageAsync(request, cancellationToken);
    }

    private static AuditRecordStreamRequest StreamRequest(
        DateTimeOffset afterStoredTime,
        Guid afterEventId,
        DateTimeOffset storedBefore,
        int limit = 10,
        string tenantId = "tenant-1"
    ) =>
        new()
        {
            TenantId = tenantId,
            AfterStoredTime = afterStoredTime,
            AfterEventId = afterEventId,
            StoredBefore = storedBefore,
            Limit = limit,
        };

    [Fact]
    public async Task GetStreamPageAsync_Always_ReadsOldestFirstAfterThePosition()
    {
        await using var provider = CreateInMemoryProvider();
        var first = Record(eventTime: Now.AddDays(-1));
        var second = Record(eventTime: Now.AddDays(-3));
        var third = Record(eventTime: Now.AddDays(-2));
        var firstTime = Now.AddMinutes(1);
        foreach (var (record, minutes) in new[] { (first, 1), (second, 2), (third, 3) })
        {
            TimeProvider.SetUtcNow(Now.AddMinutes(minutes));
            await AddAsync(provider, [record], CancellationToken.None);
        }
        var storedBefore = Now.AddHours(1);

        var page = await GetStreamPageAsync(
            provider,
            StreamRequest(firstTime, first.EventId, storedBefore),
            CancellationToken.None
        );

        Assert.Equal([second.EventId, third.EventId], page.Items.Select(item => item.EventId));
        Assert.Equal(Now.AddMinutes(3), page.LastStoredTime);
        Assert.Equal(third.EventId, page.LastEventId);
    }

    [Fact]
    public async Task GetStreamPageAsync_Always_SkipsRecordsStoredAtOrAfterStoredBefore()
    {
        await using var provider = CreateInMemoryProvider();
        var before = Record();
        var at = Record();
        var after = Record();
        foreach (var (record, minutes) in new[] { (before, 1), (at, 2), (after, 3) })
        {
            TimeProvider.SetUtcNow(Now.AddMinutes(minutes));
            await AddAsync(provider, [record], CancellationToken.None);
        }

        var page = await GetStreamPageAsync(
            provider,
            StreamRequest(Now.AddDays(-1), Guid.Empty, storedBefore: Now.AddMinutes(2)),
            CancellationToken.None
        );

        Assert.Equal(before.EventId, Assert.Single(page.Items).EventId);
    }

    [Fact]
    public async Task GetStreamPageAsync_Always_ReadsOnlyTheTenantsRecords()
    {
        await using var provider = CreateInMemoryProvider();
        var mine = Record();
        var other = Record(tenantId: "tenant-2");
        await AddAsync(provider, [mine, other], CancellationToken.None);

        var page = await GetStreamPageAsync(
            provider,
            StreamRequest(Now.AddDays(-1), Guid.Empty, Now.AddHours(1)),
            CancellationToken.None
        );

        Assert.Equal(mine.EventId, Assert.Single(page.Items).EventId);
    }

    [Fact]
    public async Task GetStreamPageAsync_WhenNothingFollows_ReturnsAnEmptyPage()
    {
        await using var provider = CreateInMemoryProvider();
        var record = Record();
        await AddAsync(provider, [record], CancellationToken.None);

        var page = await GetStreamPageAsync(
            provider,
            StreamRequest(Now, record.EventId, Now.AddHours(1)),
            CancellationToken.None
        );

        Assert.Empty(page.Items);
        Assert.Null(page.LastStoredTime);
        Assert.Null(page.LastEventId);
    }

    [Fact]
    public async Task GetStreamPageAsync_WithTheSameStoredTime_OrdersByEventId()
    {
        await using var provider = CreateInMemoryProvider();
        var ordered = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() }
            .Order()
            .ToList();
        await AddAsync(
            provider,
            [Record(ordered[2]), Record(ordered[0]), Record(ordered[1])],
            CancellationToken.None
        );

        var page = await GetStreamPageAsync(
            provider,
            StreamRequest(Now, ordered[0], Now.AddHours(1), limit: 1),
            CancellationToken.None
        );

        Assert.Equal(ordered[1], Assert.Single(page.Items).EventId);
        Assert.Equal(Now, page.LastStoredTime);
        Assert.Equal(ordered[1], page.LastEventId);
    }

    #endregion

    #region GetPageAsync Tests

    private static async ValueTask<PagedResult<PersistedAuditRecord>> GetPageAsync(
        ServiceProvider provider,
        AuditRecordFilter filter,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    )
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = new AuditRecordStore(
            Mock.Of<IStoreProvider>(),
            provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            context,
            new FakeTimeProvider(Now)
        );
        return await store.GetPageAsync(
            filter,
            new PageRequest { Cursor = cursor, Limit = limit },
            cancellationToken
        );
    }

    [Fact]
    public async Task GetPageAsync_ReturnsOnlyTheTenantsRecordsThatMatchEveryCriterion()
    {
        await using var provider = CreateInMemoryProvider();
        var match = Record(eventTime: Now.AddHours(-1));
        var otherTenant = new PersistedAuditRecord
        {
            EventId = Guid.CreateVersion7(),
            EventTime = Now.AddHours(-1),
            EventName = match.EventName,
            EventCategory = match.EventCategory,
            Outcome = match.Outcome,
            Severity = match.Severity,
            Cell = match.Cell,
            TenantId = "tenant-2",
            ClientId = match.ClientId,
            ActorType = match.ActorType,
            Resources = [],
            Record = JsonElements.EmptyObject,
        };
        var tooOld = Record(eventTime: Now.AddDays(-2));
        await AddAsync(provider, [match, otherTenant, tooOld], CancellationToken.None);

        var page = await GetPageAsync(
            provider,
            new AuditRecordFilter
            {
                TenantId = "tenant-1",
                StartTime = Now.AddDays(-1),
                EndTime = Now,
                EventName = "client.updated",
                ClientId = "client-1",
                ActorId = "admin-1",
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            },
            cursor: null,
            limit: 10,
            CancellationToken.None
        );

        Assert.Equal(match.EventId, Assert.Single(page.Items).EventId);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetPageAsync_MatchesAResourceAnywhereInTheRecord()
    {
        await using var provider = CreateInMemoryProvider();
        var owner = Record(
            resources:
            [
                new PersistedAuditResource { Type = "client", Id = "client-1" },
                new PersistedAuditResource { Type = "tenant", Id = "tenant-1" },
            ]
        );
        await AddAsync(provider, [owner, Record()], CancellationToken.None);

        var byOwner = await GetPageAsync(
            provider,
            new AuditRecordFilter
            {
                TenantId = "tenant-1",
                ResourceType = "tenant",
                ResourceId = "tenant-1",
            },
            cursor: null,
            limit: 10,
            CancellationToken.None
        );
        var mismatched = await GetPageAsync(
            provider,
            new AuditRecordFilter
            {
                TenantId = "tenant-1",
                ResourceType = "tenant",
                ResourceId = "client-1",
            },
            cursor: null,
            limit: 10,
            CancellationToken.None
        );

        var item = Assert.Single(byOwner.Items);
        Assert.Equal(owner.EventId, item.EventId);
        Assert.Equal(["client", "tenant"], item.Resources.Select(resource => resource.Type));
        Assert.Empty(mismatched.Items);
    }

    [Fact]
    public async Task GetPageAsync_PagesNewestFirst()
    {
        await using var provider = CreateInMemoryProvider();
        var records = new List<PersistedAuditRecord>();
        for (var i = 0; i < 3; i++)
        {
            var record = Record();
            records.Add(record);
            await AddAsync(provider, [record], CancellationToken.None);
        }
        var filter = new AuditRecordFilter { TenantId = "tenant-1" };

        var first = await GetPageAsync(provider, filter, null, 2, CancellationToken.None);
        var second = await GetPageAsync(
            provider,
            filter,
            first.NextCursor,
            2,
            CancellationToken.None
        );

        Assert.Equal(
            [records[2].EventId, records[1].EventId],
            first.Items.Select(item => item.EventId)
        );
        Assert.NotNull(first.NextCursor);
        Assert.Equal(records[0].EventId, Assert.Single(second.Items).EventId);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task GetPageAsync_PagesByEventTimeNotWriteOrder()
    {
        await using var provider = CreateInMemoryProvider();
        var older = Record(eventTime: Now.AddMinutes(-1));
        var newest = Record(eventTime: Now);
        var oldest = Record(eventTime: Now.AddMinutes(-2));
        foreach (var record in new[] { older, newest, oldest })
            await AddAsync(provider, [record], CancellationToken.None);
        var filter = new AuditRecordFilter { TenantId = "tenant-1" };

        var first = await GetPageAsync(provider, filter, null, 2, CancellationToken.None);
        var second = await GetPageAsync(
            provider,
            filter,
            first.NextCursor,
            2,
            CancellationToken.None
        );

        Assert.Equal([newest.EventId, older.EventId], first.Items.Select(item => item.EventId));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(oldest.EventId, Assert.Single(second.Items).EventId);
        Assert.Null(second.NextCursor);
    }

    private async ValueTask AssertCursorStartsFromBeginningAsync(
        string cursor,
        CancellationToken cancellationToken
    )
    {
        await using var provider = CreateInMemoryProvider();
        for (var i = 0; i < 3; i++)
        {
            var record = Record();
            await AddAsync(provider, [record], cancellationToken);
        }
        var filter = new AuditRecordFilter { TenantId = "tenant-1" };
        var first = await GetPageAsync(provider, filter, null, 2, cancellationToken);

        var page = await GetPageAsync(provider, filter, cursor, 2, cancellationToken);
        var expectedEventIds = first.Items.Select(item => item.EventId).ToList();
        var actualEventIds = page.Items.Select(item => item.EventId).ToList();

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(expectedEventIds, actualEventIds);
    }

    private static string ProtectPosition(string collection, int length, long ticks)
    {
        var position = new byte[length];
        BinaryPrimitives.WriteInt64BigEndian(position, ticks);
        return TestPageCursorProtector.Instance.Protect(collection, position);
    }

    [Fact]
    public async Task GetPageAsync_WhenCursorIsNotProtected_StartsFromBeginning()
    {
        await AssertCursorStartsFromBeginningAsync("not-a-cursor", CancellationToken.None);
    }

    [Fact]
    public async Task GetPageAsync_WhenCursorIsForAnotherCollection_StartsFromBeginning()
    {
        var cursor = ProtectPosition("ClientEntity", length: 24, Now.UtcTicks);

        await AssertCursorStartsFromBeginningAsync(cursor, CancellationToken.None);
    }

    [Fact]
    public async Task GetPageAsync_WhenCursorPositionHasAnotherLength_StartsFromBeginning()
    {
        var cursor = ProtectPosition(nameof(AuditRecordEntity), length: 16, Now.UtcTicks);

        await AssertCursorStartsFromBeginningAsync(cursor, CancellationToken.None);
    }

    [Fact]
    public async Task GetPageAsync_WhenCursorTicksAreOutOfRange_StartsFromBeginning()
    {
        var cursor = ProtectPosition(nameof(AuditRecordEntity), length: 24, long.MaxValue);

        await AssertCursorStartsFromBeginningAsync(cursor, CancellationToken.None);
    }

    #endregion
    #region RemoveBeforeAsync Tests

    [Fact]
    public async Task RemoveBeforeAsync_RemovesTheRecordsPastTheCutoffWithTheirResources()
    {
        await using var provider = CreateInMemoryProvider();
        var resource = new PersistedAuditResource { Type = "client", Id = "client-1" };
        await AddAsync(
            provider,
            [
                Record(eventTime: Now.AddDays(-91), resources: [resource]),
                Record(eventTime: Now.AddDays(-90).AddSeconds(-1), resources: [resource]),
                Record(eventTime: Now.AddDays(-89), resources: [resource]),
            ],
            CancellationToken.None
        );

        var removed = await RemoveBeforeAsync(
            provider,
            Now.AddDays(-90),
            limit: 10,
            CancellationToken.None
        );

        Assert.Equal(2, removed);
        Assert.Equal((1, 1), await CountAsync(provider, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveBeforeAsync_RemovesAtMostTheLimit()
    {
        await using var provider = CreateInMemoryProvider();
        await AddAsync(
            provider,
            [
                Record(eventTime: Now.AddDays(-100)),
                Record(eventTime: Now.AddDays(-100)),
                Record(eventTime: Now.AddDays(-100)),
            ],
            CancellationToken.None
        );

        var first = await RemoveBeforeAsync(provider, Now, limit: 2, CancellationToken.None);
        var second = await RemoveBeforeAsync(provider, Now, limit: 2, CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal((0, 0), await CountAsync(provider, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveBeforeAsync_WhenTheLimitIsBelowOne_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await RemoveBeforeAsync(Provider, Now, limit: 0, CancellationToken.None)
        );
    }

    #endregion
}
