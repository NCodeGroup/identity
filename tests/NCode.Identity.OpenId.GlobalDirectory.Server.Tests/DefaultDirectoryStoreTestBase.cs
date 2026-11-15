// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
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
        Provider = serviceCollection.BuildServiceProvider();
        DbContextFactory = Provider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();

        using (var dbContext = DbContextFactory.CreateDbContext())
        {
            dbContext.Database.EnsureCreated();
        }

        Store = new DefaultDirectoryStore(DbContextFactory, TimeProvider);
    }

    protected ITenantDirectory Tenants => Store;

    protected ITeamDirectory Teams => Store;

    protected ICellDirectory Cells => Store;

    protected IPlatformDirectory Platform => Store;

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
        string issuer,
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
        string issuer,
        string? teamId = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await ReserveAsync(tenantId, issuer, teamId, cancellationToken);
        Assert.True(result.IsReserved);

        var placementResult = await Tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = tenantId,
                Issuer = issuer,
                Cell = CellName,
                Region = CellRegion,
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
                ParentTeamId = parentTeamId,
                SettingsJson = SettingsWithLimit(maxTenants),
            },
            cancellationToken
        );

    protected ValueTask PublishPlatformAsync(
        int? maxTenants,
        CancellationToken cancellationToken = default
    ) =>
        Platform.PublishAsync(
            new PlatformDocument { SettingsJson = SettingsWithLimit(maxTenants) },
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

    protected async ValueTask<int> CountPlatformRowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Platforms.CountAsync(cancellationToken);
    }
}
