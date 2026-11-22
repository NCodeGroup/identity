// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Runs on the EF InMemory provider, because EF's SQLite provider cannot compare a DateTimeOffset in a query, which the
// due-state read does. The store checks the concurrency tokens itself, so the stale-token tests hold on every provider.
public sealed class AuditStreamStoreTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string TenantC = "tenant-c";

    private static DateTimeOffset Now { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider Provider { get; }
    private TestAmbientTenantAccessor AmbientTenantAccessor { get; } = new();

    public AuditStreamStoreTests()
    {
        // The accessor is registered before the shared setup, which keeps the first registration; the tests start
        // unscoped and a test that checks the tenant scope picks a tenant.
        var databaseName = Guid.NewGuid().ToString("N");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(AmbientTenantAccessor);
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        Provider = serviceCollection.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = scope.ServiceProvider.GetRequiredService<ISurrogateKeyGenerator>();
        context.SeedTenant(keyGenerator, TenantA);
        context.SeedTenant(keyGenerator, TenantB);
        context.SeedTenant(keyGenerator, TenantC);
    }

    public void Dispose()
    {
        Provider.Dispose();
    }

    #region Helpers

    private AuditStreamStore CreateStore(OpenIdDbContext context) =>
        new(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            context
        );

    private static PersistedAuditStreamSigningSecret CreateSecret(
        string secretId,
        string protectedValue = "protected-value",
        DateTimeOffset? expiryTime = null
    ) =>
        new()
        {
            SecretId = secretId,
            ProtectedValue = protectedValue,
            CreatedTime = Now,
            ExpiryTime = expiryTime,
        };

    private static PersistedAuditStream CreateStream(
        string streamId,
        string tenantId = TenantA,
        bool isEnabled = true,
        DateTimeOffset? createdTime = null,
        params PersistedAuditStreamSigningSecret[] secrets
    ) =>
        new()
        {
            TenantId = tenantId,
            StreamId = streamId,
            DisplayName = $"Stream {streamId}",
            Endpoint = $"https://siem.example/{streamId}",
            IsEnabled = isEnabled,
            CreatedTime = createdTime ?? Now,
            ConcurrencyToken = string.Empty,
            SigningSecrets = secrets,
        };

    private async ValueTask AddAsync(
        PersistedAuditStream stream,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        await CreateStore(context).AddAsync(stream, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    // Runs a write in its own unit of work and saves it, so the next read starts from the stored rows.
    private async ValueTask WriteAsync(
        Func<AuditStreamStore, ValueTask> write,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        await write(CreateStore(context));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask<T> ReadAsync<T>(
        Func<AuditStreamStore, ValueTask<T>> read,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        cancellationToken.ThrowIfCancellationRequested();
        return await read(CreateStore(context));
    }

    private async ValueTask<PersistedAuditStream> GetAsync(
        string streamId,
        CancellationToken cancellationToken
    )
    {
        var stream = await ReadAsync(
            store => store.GetOrDefaultAsync(streamId, cancellationToken),
            cancellationToken
        );
        return Assert.IsType<PersistedAuditStream>(stream);
    }

    private async ValueTask UpdateStateAsync(
        string streamId,
        Action<PersistedAuditStreamState> change,
        CancellationToken cancellationToken
    )
    {
        var stream = await GetAsync(streamId, cancellationToken);
        var state = Assert.IsType<PersistedAuditStreamState>(stream.State);
        change(state);
        await WriteAsync(
            store => store.UpdateStateAsync(state, cancellationToken),
            cancellationToken
        );
    }

    // Stores a record of the tenant at the given stored time, as the audit record store stamps it, and returns its event
    // identifier.
    private async ValueTask<Guid> AddRecordAsync(
        string tenantId,
        DateTimeOffset storedTime,
        CancellationToken cancellationToken
    )
    {
        var record = new PersistedAuditRecord
        {
            EventId = Guid.CreateVersion7(),
            EventTime = storedTime,
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
            Resources = [],
            Record = JsonElement.Parse("""{"schema_version":"1.0"}"""),
        };

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var recordStore = new AuditRecordStore(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            context,
            new FakeTimeProvider(storedTime)
        );
        await recordStore.AddAsync([record], cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return record.EventId;
    }

    private async ValueTask<int> CountRowsAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : class
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await context.Set<TEntity>().CountAsync(cancellationToken);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_Always_StoresTheStreamItsSecretsAndAStateDueAtCreation()
    {
        var createdTime = Now.AddMinutes(-5);
        await AddAsync(
            CreateStream(
                "stream-1",
                createdTime: createdTime,
                secrets: [CreateSecret("secret-1", "value-1"), CreateSecret("secret-2", "value-2")]
            ),
            CancellationToken.None
        );

        var stream = await GetAsync("stream-1", CancellationToken.None);

        Assert.Equal(TenantA, stream.TenantId);
        Assert.Equal("Stream stream-1", stream.DisplayName);
        Assert.Equal("https://siem.example/stream-1", stream.Endpoint);
        Assert.True(stream.IsEnabled);
        Assert.Equal(createdTime, stream.CreatedTime);
        Assert.NotEmpty(stream.ConcurrencyToken);
        Assert.Equal(
            new[] { "secret-1:value-1", "secret-2:value-2" },
            stream.SigningSecrets.Select(secret => $"{secret.SecretId}:{secret.ProtectedValue}")
        );

        var state = Assert.IsType<PersistedAuditStreamState>(stream.State);
        Assert.Equal(TenantA, state.TenantId);
        Assert.Equal("stream-1", state.StreamId);
        Assert.Equal(createdTime, state.PositionStoredTime);
        Assert.Equal(Guid.Empty, state.PositionEventId);
        Assert.Equal(createdTime, state.NextAttemptTime);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Null(state.LeaseHolder);
        Assert.NotEmpty(state.ConcurrencyToken);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_ForAnotherTenantsStream_ReturnsNull()
    {
        await AddAsync(CreateStream("stream-b", TenantB), CancellationToken.None);
        AmbientTenantAccessor.UseTenant(TenantA);

        var stream = await ReadAsync(
            store => store.GetOrDefaultAsync("stream-b", CancellationToken.None),
            CancellationToken.None
        );

        Assert.Null(stream);
    }

    #endregion

    #region GetPageAsync Tests

    // The page follows the surrogate keys, which the sequential generator mints in the order the streams are added.
    [Fact]
    public async Task GetPageAsync_Always_PagesInInsertionOrder()
    {
        await AddAsync(CreateStream("stream-c"), CancellationToken.None);
        await AddAsync(CreateStream("stream-a"), CancellationToken.None);
        await AddAsync(CreateStream("stream-b"), CancellationToken.None);

        var first = await ReadAsync(
            store => store.GetPageAsync(new PageRequest { Limit = 2 }, CancellationToken.None),
            CancellationToken.None
        );
        var second = await ReadAsync(
            store =>
                store.GetPageAsync(
                    new PageRequest { Limit = 2, Cursor = first.NextCursor },
                    CancellationToken.None
                ),
            CancellationToken.None
        );

        Assert.Equal(
            new[] { "stream-c", "stream-a", "stream-b" },
            first.Items.Concat(second.Items).Select(stream => stream.StreamId)
        );
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_Always_CountsTheTenantsStreams()
    {
        await AddAsync(CreateStream("stream-1"), CancellationToken.None);
        await AddAsync(CreateStream("stream-2"), CancellationToken.None);
        await AddAsync(CreateStream("stream-3", TenantB), CancellationToken.None);
        AmbientTenantAccessor.UseTenant(TenantA);

        var count = await ReadAsync(
            store => store.CountAsync(CancellationToken.None),
            CancellationToken.None
        );

        Assert.Equal(2, count);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithAStaleConcurrencyToken_Throws()
    {
        await AddAsync(CreateStream("stream-1"), CancellationToken.None);
        var stream = await GetAsync("stream-1", CancellationToken.None);
        stream.ConcurrencyToken = "stale";

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await WriteAsync(
                store => store.UpdateAsync(stream, CancellationToken.None),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task UpdateAsync_Always_ReplacesTheSecrets()
    {
        await AddAsync(
            CreateStream(
                "stream-1",
                secrets: [CreateSecret("secret-1"), CreateSecret("secret-2", "value-2")]
            ),
            CancellationToken.None
        );
        var stream = await GetAsync("stream-1", CancellationToken.None);
        stream.SigningSecrets =
        [
            CreateSecret("secret-2", "value-2-rewritten", Now.AddHours(24)),
            CreateSecret("secret-3", "value-3"),
        ];

        await WriteAsync(
            store => store.UpdateAsync(stream, CancellationToken.None),
            CancellationToken.None
        );

        var updated = await GetAsync("stream-1", CancellationToken.None);
        Assert.Equal(
            new[] { "secret-2:value-2-rewritten", "secret-3:value-3" },
            updated
                .SigningSecrets.OrderBy(secret => secret.SecretId, StringComparer.Ordinal)
                .Select(secret => $"{secret.SecretId}:{secret.ProtectedValue}")
        );
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_Always_RemovesTheStateAndTheSecrets()
    {
        await AddAsync(
            CreateStream("stream-1", secrets: [CreateSecret("secret-1"), CreateSecret("secret-2")]),
            CancellationToken.None
        );

        await WriteAsync(
            async store =>
            {
                var removed = await store.RemoveAsync("stream-1", CancellationToken.None);
                Assert.True(removed);
            },
            CancellationToken.None
        );

        var rows = (
            await CountRowsAsync<AuditStreamEntity>(CancellationToken.None),
            await CountRowsAsync<AuditStreamStateEntity>(CancellationToken.None),
            await CountRowsAsync<AuditStreamSigningSecretEntity>(CancellationToken.None)
        );
        Assert.Equal((0, 0, 0), rows);
    }

    #endregion

    #region GetDueStatesAsync Tests

    [Fact]
    public async Task GetDueStatesAsync_Always_SkipsDisabledLeasedAndFutureStates()
    {
        var created = Now.AddHours(-1);
        await AddAsync(CreateStream("due", createdTime: created), CancellationToken.None);
        await AddAsync(
            CreateStream("disabled", isEnabled: false, createdTime: created),
            CancellationToken.None
        );
        await AddAsync(CreateStream("leased", createdTime: created), CancellationToken.None);
        await AddAsync(CreateStream("future", createdTime: created), CancellationToken.None);
        await AddAsync(
            CreateStream("expired-lease", TenantB, createdTime: created),
            CancellationToken.None
        );
        await UpdateStateAsync(
            "leased",
            state =>
            {
                state.LeaseHolder = "replica-1";
                state.LeaseExpiryTime = Now.AddMinutes(2);
            },
            CancellationToken.None
        );
        await UpdateStateAsync(
            "future",
            state => state.NextAttemptTime = Now.AddMinutes(5),
            CancellationToken.None
        );
        await UpdateStateAsync(
            "expired-lease",
            state =>
            {
                state.NextAttemptTime = Now.AddMinutes(-10);
                state.LeaseHolder = "replica-1";
                state.LeaseExpiryTime = Now.AddMinutes(-1);
            },
            CancellationToken.None
        );
        var recordTime = created.AddMinutes(10);
        await AddRecordAsync(TenantA, recordTime, CancellationToken.None);
        await AddRecordAsync(TenantB, recordTime, CancellationToken.None);

        var states = await ReadAsync(
            store => store.GetDueStatesAsync(Now, Now, 10, CancellationToken.None),
            CancellationToken.None
        );

        Assert.Equal(
            new[] { (TenantA, "due"), (TenantB, "expired-lease") },
            states.Select(state => (state.TenantId, state.StreamId))
        );
    }

    [Fact]
    public async Task GetDueStatesAsync_WhenNoRecordIsReadyAfterThePosition_SkipsTheStream()
    {
        var created = Now.AddHours(-1);
        var recordTime = created.AddMinutes(10);
        await AddAsync(CreateStream("behind", createdTime: created), CancellationToken.None);
        await AddAsync(CreateStream("caught-up", createdTime: created), CancellationToken.None);
        await AddAsync(CreateStream("ahead", createdTime: created), CancellationToken.None);
        await AddAsync(
            CreateStream("unsettled", TenantB, createdTime: created),
            CancellationToken.None
        );
        await AddAsync(CreateStream("idle", TenantC, createdTime: created), CancellationToken.None);

        var recordEventId = await AddRecordAsync(TenantA, recordTime, CancellationToken.None);
        await AddRecordAsync(TenantB, Now, CancellationToken.None);
        await UpdateStateAsync(
            "caught-up",
            state =>
            {
                state.PositionStoredTime = recordTime;
                state.PositionEventId = recordEventId;
            },
            CancellationToken.None
        );
        await UpdateStateAsync(
            "ahead",
            state => state.PositionStoredTime = recordTime.AddMinutes(1),
            CancellationToken.None
        );

        var states = await ReadAsync(
            store => store.GetDueStatesAsync(Now, Now, 10, CancellationToken.None),
            CancellationToken.None
        );

        Assert.Equal(
            new[] { (TenantA, "behind") },
            states.Select(state => (state.TenantId, state.StreamId))
        );
    }

    #endregion

    #region UpdateStateAsync Tests

    [Fact]
    public async Task UpdateStateAsync_Always_RotatesTheStoredConcurrencyToken()
    {
        await AddAsync(CreateStream("stream-1"), CancellationToken.None);
        var before = await GetAsync("stream-1", CancellationToken.None);
        var state = Assert.IsType<PersistedAuditStreamState>(before.State);
        var carriedToken = state.ConcurrencyToken;
        state.ConsecutiveFailures = 1;

        await WriteAsync(
            store => store.UpdateStateAsync(state, CancellationToken.None),
            CancellationToken.None
        );

        var after = await GetAsync("stream-1", CancellationToken.None);
        var stored = Assert.IsType<PersistedAuditStreamState>(after.State);
        Assert.NotEqual(carriedToken, stored.ConcurrencyToken);
    }

    [Fact]
    public async Task UpdateStateAsync_WithAStaleConcurrencyToken_Throws()
    {
        await AddAsync(CreateStream("stream-1"), CancellationToken.None);
        var stream = await GetAsync("stream-1", CancellationToken.None);
        var state = Assert.IsType<PersistedAuditStreamState>(stream.State);
        state.ConcurrencyToken = "stale";

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await WriteAsync(
                store => store.UpdateStateAsync(state, CancellationToken.None),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task UpdateStateAsync_WhenTheStreamWasRemoved_Throws()
    {
        await AddAsync(CreateStream("stream-1"), CancellationToken.None);
        var stream = await GetAsync("stream-1", CancellationToken.None);
        var state = Assert.IsType<PersistedAuditStreamState>(stream.State);
        await WriteAsync(
            async store => await store.RemoveAsync("stream-1", CancellationToken.None),
            CancellationToken.None
        );

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await WriteAsync(
                store => store.UpdateStateAsync(state, CancellationToken.None),
                CancellationToken.None
            )
        );
    }

    #endregion

    #region GetProtectedSigningSecretPageAsync Tests

    [Fact]
    public async Task GetProtectedSigningSecretPageAsync_Always_ReturnsOnlyValuesWithTheRequestedPrefixes()
    {
        await AddAsync(
            CreateStream(
                "stream-1",
                secrets:
                [
                    CreateSecret("secret-1", "key-1:aaa"),
                    CreateSecret("secret-2", "key-2:bbb"),
                    CreateSecret("secret-3", "key-1:ccc"),
                ]
            ),
            CancellationToken.None
        );

        var page = await ReadAsync(
            store =>
                store.GetProtectedSigningSecretPageAsync(
                    TenantA,
                    ["key-1:"],
                    new PageRequest { Limit = 10 },
                    CancellationToken.None
                ),
            CancellationToken.None
        );

        Assert.Equal(
            new[] { "stream-1/secret-1", "stream-1/secret-3" },
            page.Items.Select(value => $"{value.RecordId}/{value.ItemId}")
                .OrderBy(value => value, StringComparer.Ordinal)
        );
    }

    #endregion

    private sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
    {
        public string? TenantId { get; private set; }

        public bool IsScoped => TenantId is not null;

        public bool IsUnscoped { get; private set; } = true;

        public string? UnscopedReason => IsUnscoped ? "test" : null;

        public void UseTenant(string tenantId)
        {
            TenantId = tenantId;
            IsUnscoped = false;
        }

        public IDisposable BeginScope(string tenantId) => throw new NotSupportedException();

        public IDisposable BeginUnscoped(string reason) => throw new NotSupportedException();
    }
}
