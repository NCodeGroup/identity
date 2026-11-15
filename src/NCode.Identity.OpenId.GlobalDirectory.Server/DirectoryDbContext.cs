// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides the entity framework <see cref="DbContext"/> of the global directory server: the cells, the tenant and team
/// identifiers with their placements, the offers to move a tenant, the published team and platform documents, the
/// retired issuers and the change
/// sequence that the cells mirror. The directory holds no personal data.
/// </summary>
/// <param name="options">The <see cref="DbContextOptions{TContext}"/> of the context.</param>
public sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options)
    : DbContext(options)
{
    // The same bounds as the cell's own identifiers: a tenant or team identifier is at most
    // DirectoryLimits.MaxIdentifierLength characters, an issuer at most DirectoryLimits.MaxIssuerLength, and a cell
    // name and a region at most CellLabelRules.MaxLabelLength.
    private const int MaxStateLength = 16;

    /// <summary>
    /// Gets the cells that registered themselves.
    /// </summary>
    public DbSet<CellRecord> Cells => Set<CellRecord>();

    /// <summary>
    /// Gets the tenant identifiers that the directory reserved, placed or retired.
    /// </summary>
    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();

    /// <summary>
    /// Gets the offers that cells record to move a tenant to another cell, one per tenant at most.
    /// </summary>
    public DbSet<TenantMoveOfferRecord> TenantMoveOffers => Set<TenantMoveOfferRecord>();

    /// <summary>
    /// Gets the team identifiers that the directory reserved, published or retired.
    /// </summary>
    public DbSet<TeamRecord> Teams => Set<TeamRecord>();

    /// <summary>
    /// Gets the platform document, which holds at most one row.
    /// </summary>
    public DbSet<PlatformRecord> Platforms => Set<PlatformRecord>();

    /// <summary>
    /// Gets the issuers that tenants gave up.
    /// </summary>
    public DbSet<RetiredIssuerRecord> RetiredIssuers => Set<RetiredIssuerRecord>();

    /// <summary>
    /// Gets the change sequence over team publishes, team retires and platform publishes.
    /// </summary>
    public DbSet<DirectoryChangeRecord> Changes => Set<DirectoryChangeRecord>();

    /// <summary>
    /// Gets the counter that numbers the changes, which holds one row once the directory writes its first change.
    /// </summary>
    public DbSet<DirectorySequenceRecord> Sequences => Set<DirectorySequenceRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CellRecord>(builder =>
        {
            builder.HasKey(entity => entity.Name);
            builder.Property(entity => entity.Name).HasMaxLength(CellLabelRules.MaxLabelLength);
            builder.Property(entity => entity.Region).HasMaxLength(CellLabelRules.MaxLabelLength);
            builder.Property(entity => entity.Kind).HasMaxLength(DirectoryLimits.MaxCellKindLength);
            builder
                .Property(entity => entity.ManagementUrl)
                .HasMaxLength(DirectoryLimits.MaxManagementUrlLength);
        });

        modelBuilder.Entity<TenantRecord>(builder =>
        {
            builder.HasIndex(entity => entity.NormalizedTenantId).IsUnique();
            builder.HasIndex(entity => entity.NormalizedIssuer).IsUnique();
            builder.HasIndex(entity => entity.NormalizedTeamId);
            builder
                .Property(entity => entity.TenantId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder
                .Property(entity => entity.NormalizedTenantId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder.Property(entity => entity.Issuer).HasMaxLength(DirectoryLimits.MaxIssuerLength);
            builder
                .Property(entity => entity.NormalizedIssuer)
                .HasMaxLength(DirectoryLimits.MaxIssuerLength);
            builder
                .Property(entity => entity.NormalizedTeamId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder.Property(entity => entity.Cell).HasMaxLength(CellLabelRules.MaxLabelLength);
            builder.Property(entity => entity.State).HasMaxLength(MaxStateLength);
        });

        modelBuilder.Entity<TenantMoveOfferRecord>(builder =>
        {
            builder.HasIndex(entity => entity.NormalizedTenantId).IsUnique();
            builder
                .Property(entity => entity.NormalizedTenantId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder.Property(entity => entity.FromCell).HasMaxLength(CellLabelRules.MaxLabelLength);
            builder.Property(entity => entity.ToCell).HasMaxLength(CellLabelRules.MaxLabelLength);
        });

        modelBuilder.Entity<TeamRecord>(builder =>
        {
            builder.HasIndex(entity => entity.NormalizedTeamId).IsUnique();
            builder.HasIndex(entity => entity.NormalizedParentTeamId);
            builder.HasIndex(entity => entity.NormalizedPath);
            builder
                .Property(entity => entity.TeamId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder
                .Property(entity => entity.NormalizedTeamId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder
                .Property(entity => entity.NormalizedParentTeamId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
            builder
                .Property(entity => entity.NormalizedPath)
                .HasMaxLength(DirectoryLimits.MaxTeamPathLength);
            builder.Property(entity => entity.State).HasMaxLength(MaxStateLength);
        });

        modelBuilder.Entity<PlatformRecord>(builder =>
        {
            builder.Property(entity => entity.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<RetiredIssuerRecord>(builder =>
        {
            builder.HasIndex(entity => entity.NormalizedIssuer).IsUnique();
            builder.Property(entity => entity.Issuer).HasMaxLength(DirectoryLimits.MaxIssuerLength);
            builder
                .Property(entity => entity.NormalizedIssuer)
                .HasMaxLength(DirectoryLimits.MaxIssuerLength);
            builder
                .Property(entity => entity.TenantId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
        });

        modelBuilder.Entity<DirectoryChangeRecord>(builder =>
        {
            builder.HasKey(entity => entity.Sequence);
            builder.Property(entity => entity.Sequence).ValueGeneratedNever();
            builder
                .Property(entity => entity.TeamId)
                .HasMaxLength(DirectoryLimits.MaxIdentifierLength);
        });

        modelBuilder.Entity<DirectorySequenceRecord>(builder =>
        {
            builder.Property(entity => entity.Id).ValueGeneratedNever();
        });
    }
}
