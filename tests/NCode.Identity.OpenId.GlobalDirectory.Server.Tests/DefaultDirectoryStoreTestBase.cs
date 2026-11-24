// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides a <see cref="DefaultDirectoryStore"/> over an in-memory Sqlite database, and the helpers that the store's
/// tests share.
/// </summary>
public abstract class DefaultDirectoryStoreTestBase : IDisposable
{
    protected const string CellName = "cell-a";
    protected const string CellRegion = "region-a";
    protected const string TenantId = "tenant-1";
    protected const string OtherTenantId = "tenant-2";
    protected const string Issuer = "https://op.example/tenant-1";
    protected const string OtherIssuer = "https://op.example/tenant-2";
    protected const string FreeIssuer = "https://op.example/tenant-3";
    protected const string ControlPlaneCell = "control";
    protected const string ControlPlaneTenantId = "root";
    protected const string ControlPlaneIssuer = "https://op.example/root";

    private static DirectoryServerOptions CreateOptions(string controlPlaneTenantId) =>
        new()
        {
            Issuer = ControlPlaneIssuer,
            ControlPlaneCell = ControlPlaneCell,
            ControlPlaneTenantId = controlPlaneTenantId,
        };

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private IDbContextFactory<DirectoryDbContext> DbContextFactory { get; }
    private DefaultDirectoryStore Store { get; }

    protected FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    protected DefaultDirectoryStoreTestBase()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(Connection)
        );
        serviceCollection.AddPersistedPayloadSerializer();
        serviceCollection.AddSurrogateKeyGenerator();
        Provider = serviceCollection.BuildServiceProvider();
        DbContextFactory = Provider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();

        using (var dbContext = DbContextFactory.CreateDbContext())
        {
            dbContext.Database.EnsureCreated();
        }

        Store = CreateStore(ControlPlaneTenantId);
    }

    private DefaultDirectoryStore CreateStore(string controlPlaneTenantId) =>
        new(
            DbContextFactory,
            Microsoft.Extensions.Options.Options.Create(CreateOptions(controlPlaneTenantId)),
            Provider.GetRequiredService<IPersistedPayloadSerializer>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TimeProvider
        );

    protected ITenantDirectory CreateTenantDirectory(string controlPlaneTenantId) =>
        CreateStore(controlPlaneTenantId);

    protected ITenantDirectory Tenants => Store;

    internal IDirectoryTenantWriter TenantWriter => Store;

    protected ITeamDirectory Teams => Store;

    internal IDirectoryTeamWriter TeamWriter => Store;

    protected ICellDirectory Cells => Store;

    protected IPlatformDirectory Platform => Store;

    protected IReplicatedDocumentDirectory Replicated => Store;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        Provider.Dispose();
        Connection.Dispose();
    }

    protected static string SettingsWithLimit(int? maxTenants) =>
        maxTenants is { } limit
            ? JsonSerializer.Serialize(new Dictionary<string, object> { ["max_tenants"] = limit })
            : "{}";

    protected ValueTask<ReservationResult> ReserveAsync(
        string tenantId,
        string? issuer,
        string? teamId = null,
        CancellationToken cancellationToken = default
    ) =>
        Tenants.ReserveAsync(
            new TenantReservation
            {
                TenantId = tenantId,
                Issuer = issuer,
                TeamId = teamId,
                Cell = CellName,
            },
            cancellationToken
        );

    protected async ValueTask ReserveAndPlaceAsync(
        string tenantId,
        string? issuer,
        string? teamId = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await ReserveAsync(tenantId, issuer, teamId, cancellationToken);
        Assert.True(result.IsReserved);

        var placementResult = await Tenants.PlaceAsync(
            new TenantPlacementRequest
            {
                TenantId = tenantId,
                Issuer = issuer,
                Cell = CellName,
            },
            cancellationToken
        );
        Assert.True(placementResult.IsReserved);
    }

    protected ValueTask PublishTeamAsync(
        string teamId,
        string? parentTeamId = null,
        int? maxTenants = null,
        CancellationToken cancellationToken = default
    ) =>
        Teams.PublishAsync(
            new TeamDocument
            {
                TeamId = teamId,
                DisplayName = teamId,
                IsDisabled = false,
                ParentTeamId = parentTeamId,
                Settings = JsonElement.Parse(SettingsWithLimit(maxTenants)),
                RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            },
            cancellationToken
        );

    protected ValueTask PublishPlatformAsync(
        int? maxTenants,
        CancellationToken cancellationToken = default
    ) =>
        Platform.PublishAsync(
            new PlatformDocument { Settings = JsonElement.Parse(SettingsWithLimit(maxTenants)) },
            cancellationToken
        );

    protected async ValueTask<TenantRecord?> ReadTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTenantId = tenantId.ToLowerInvariant();
        return await dbContext
            .Tenants.AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            );
    }

    protected async ValueTask<TenantMoveOfferRecord?> ReadMoveOfferAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTenantId = tenantId.ToLowerInvariant();
        return await dbContext
            .TenantMoveOffers.AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            );
    }

    protected async ValueTask<string?> ReadTeamPathAsync(
        string teamId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTeamId = teamId.ToLowerInvariant();
        return await dbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => entity.NormalizedPath)
            .SingleOrDefaultAsync(cancellationToken);
    }

    protected async ValueTask<int> CountPlatformRowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Platforms.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the text that the team row of a team stores as its document, or <c>null</c> when it stores none.
    /// </summary>
    protected async ValueTask<string?> ReadTeamDocumentJsonAsync(
        string teamId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTeamId = teamId.ToLowerInvariant();
        return await dbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => entity.DocumentJson)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the text that the platform row stores as its document, or <c>null</c> when no platform row exists.
    /// </summary>
    protected async ValueTask<string?> ReadPlatformDocumentJsonAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => entity.DocumentJson)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the text that the change with a sequence number stores as its document, or <c>null</c> when it stores
    /// none.
    /// </summary>
    protected async ValueTask<string?> ReadChangeDocumentJsonAsync(
        long sequence,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Changes.AsNoTracking()
            .Where(entity => entity.Sequence == sequence)
            .Select(entity => entity.DocumentJson)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a text of the test's own into the document column of a team's row, past the store.
    /// </summary>
    protected async ValueTask ReplaceTeamDocumentJsonAsync(
        string teamId,
        string documentJson,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTeamId = teamId.ToLowerInvariant();
        var team = await dbContext.Teams.SingleAsync(
            entity => entity.NormalizedTeamId == normalizedTeamId,
            cancellationToken
        );
        team.DocumentJson = documentJson;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a text of the test's own into the document column of the platform row, past the store.
    /// </summary>
    protected async ValueTask ReplacePlatformDocumentJsonAsync(
        string documentJson,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var platform = await dbContext.Platforms.SingleAsync(cancellationToken);
        platform.DocumentJson = documentJson;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a text of the test's own into the document column of the change with a sequence number, past the store.
    /// </summary>
    protected async ValueTask ReplaceChangeDocumentJsonAsync(
        long sequence,
        string documentJson,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var change = await dbContext.Changes.SingleAsync(
            entity => entity.Sequence == sequence,
            cancellationToken
        );
        change.DocumentJson = documentJson;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
