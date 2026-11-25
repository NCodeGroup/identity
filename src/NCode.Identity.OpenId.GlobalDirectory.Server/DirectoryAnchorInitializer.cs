// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Writes the platform's trust anchor when the directory starts: the control-plane cell's record, with the region, kind
/// and client key set that <see cref="DirectoryServerOptions.ControlPlane"/> names, and the placement of its root
/// tenant in that cell. It runs in one serializable transaction that changes nothing when the stored anchor already
/// matches. A stored state that conflicts with the configuration, a retired cell or a tenant identifier that another
/// cell holds or that is retired, fails the start. Once the transaction commits it sets
/// <see cref="DirectoryAnchorState.IsInitialized"/>.
/// </summary>
// See ADR-1536.
internal sealed class DirectoryAnchorInitializer(
    IDbContextFactory<DirectoryDbContext> dbContextFactory,
    IOptions<DirectoryServerOptions> options,
    ISurrogateKeyGenerator keyGenerator,
    TimeProvider timeProvider,
    DirectoryAnchorState state
) : IHostedService
{
    /// <summary>
    /// Normalizes an identifier the way the directory keys it.
    /// </summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The lower-cased identifier.</returns>
    private static string Normalize(string value) => value.ToLowerInvariant();

    /// <summary>
    /// Ensures the control-plane cell exists and matches the directory's configuration.
    /// </summary>
    /// <param name="dbContext">The directory's database context.</param>
    /// <param name="controlPlane">The control-plane options.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <exception cref="InvalidOperationException">The configured cell is retired.</exception>
    private static async ValueTask EnsureCellAsync(
        DirectoryDbContext dbContext,
        DirectoryControlPlaneOptions controlPlane,
        CancellationToken cancellationToken
    )
    {
        var cell = await dbContext.Cells.SingleOrDefaultAsync(
            entity => entity.Name == controlPlane.Cell,
            cancellationToken
        );
        if (cell is null)
        {
            var newCell = new CellRecord
            {
                Name = controlPlane.Cell,
                Region = controlPlane.Region,
                Kind = controlPlane.Kind,
                ClientJwksJson = controlPlane.ClientJwks,
                IsControlPlane = true,
            };
            dbContext.Cells.Add(newCell);
            return;
        }

        if (cell.IsRetired)
        {
            var cellKey = DirectoryControlPlaneOptions.GetConfigurationKey(
                nameof(DirectoryControlPlaneOptions.Cell)
            );
            throw new InvalidOperationException(
                $"The cell '{controlPlane.Cell}' that {cellKey} names is retired, so it cannot be the control-plane cell; set the key to a cell name that was never retired."
            );
        }

        // Only the directory's configuration decides what the anchor is, so a stored record is brought to it.
        cell.Region = controlPlane.Region;
        cell.Kind = controlPlane.Kind;
        cell.ClientJwksJson = controlPlane.ClientJwks;
        cell.IsControlPlane = true;
    }

    private IDbContextFactory<DirectoryDbContext> DbContextFactory { get; } = dbContextFactory;

    private IOptions<DirectoryServerOptions> ServerOptions { get; } = options;

    private ISurrogateKeyGenerator KeyGenerator { get; } = keyGenerator;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private DirectoryAnchorState State { get; } = state;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var controlPlane = ServerOptions.Value.ControlPlane;

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(
            async _ =>
            {
                // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                dbContext.ChangeTracker.Clear();
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken
                );

                await EnsureCellAsync(dbContext, controlPlane, cancellationToken);
                await EnsureTenantAsync(dbContext, controlPlane, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            },
            cancellationToken
        );

        State.MarkInitialized();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Ensures the control-plane tenant exists in the control-plane cell and matches the directory's configuration.
    /// </summary>
    /// <param name="dbContext">The directory's database context.</param>
    /// <param name="controlPlane">The control-plane options.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    private async ValueTask EnsureTenantAsync(
        DirectoryDbContext dbContext,
        DirectoryControlPlaneOptions controlPlane,
        CancellationToken cancellationToken
    )
    {
        var tenantId = controlPlane.TenantId;
        var normalizedTenantId = Normalize(tenantId);
        var existing = await dbContext.Tenants.SingleOrDefaultAsync(
            entity => entity.NormalizedTenantId == normalizedTenantId,
            cancellationToken
        );
        var now = TimeProvider.GetUtcNow();
        if (existing is not null)
        {
            var isHeld =
                existing.State != TenantRecord.StateRetired
                && string.Equals(existing.Cell, controlPlane.Cell, StringComparison.Ordinal);
            if (!isHeld)
            {
                var tenantKey = DirectoryControlPlaneOptions.GetConfigurationKey(
                    nameof(DirectoryControlPlaneOptions.TenantId)
                );
                throw new InvalidOperationException(
                    $"The tenant '{tenantId}' that {tenantKey} names is retired or held by another cell, so the directory cannot place it in the control-plane cell; set {tenantKey} to the control-plane cell's root tenant identifier, which no other cell holds or retired."
                );
            }

            if (existing.State != TenantRecord.StatePlaced)
            {
                existing.State = TenantRecord.StatePlaced;
                existing.ModifiedWhen = now;
            }

            return;
        }

        var issuer = controlPlane.Issuer;
        var normalizedIssuer = Normalize(issuer);
        var isIssuerHeld = await dbContext.Tenants.AnyAsync(
            entity => entity.NormalizedIssuer == normalizedIssuer,
            cancellationToken
        );
        var isIssuerRetired = await dbContext.RetiredIssuers.AnyAsync(
            entity => entity.NormalizedIssuer == normalizedIssuer,
            cancellationToken
        );
        if (isIssuerHeld || isIssuerRetired)
        {
            var issuerKey = DirectoryControlPlaneOptions.GetConfigurationKey(
                nameof(DirectoryControlPlaneOptions.Issuer)
            );
            throw new InvalidOperationException(
                $"Another tenant holds or retired the issuer that {issuerKey} names, so the directory cannot place the control-plane cell's root tenant '{tenantId}' under it; set {issuerKey} to the control-plane cell's issuer, which no other tenant holds or retired."
            );
        }

        var surrogateKey = KeyGenerator.CreateKey();
        var newTenant = new TenantRecord
        {
            Id = surrogateKey,
            TenantId = tenantId,
            NormalizedTenantId = normalizedTenantId,
            Issuer = issuer,
            NormalizedIssuer = normalizedIssuer,
            NormalizedTeamId = null,
            Cell = controlPlane.Cell,
            State = TenantRecord.StatePlaced,
            CreatedWhen = now,
            ModifiedWhen = now,
        };
        dbContext.Tenants.Add(newTenant);
    }
}
