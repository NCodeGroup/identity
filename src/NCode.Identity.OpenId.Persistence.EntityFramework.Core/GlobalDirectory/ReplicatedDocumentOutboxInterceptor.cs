// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Writes the documents that the control-plane cell replicates to the cells into the outbox, in the same save as the
/// change that touches them. It runs only when <see cref="IDirectoryOutboxPolicy.IsPublishing"/> is <c>true</c>. For
/// each touched document it composes the content whole from the database rows overlaid with the tracked changes, raises
/// the document's source revision and adds an outbox entry, so no writer can forget to publish.
/// </summary>
// See ADR-1536
internal sealed class ReplicatedDocumentOutboxInterceptor(
    IDirectoryOutboxPolicy policy,
    IDirectoryOutboxNotifier notifier,
    IPersistedPayloadSerializer payloadSerializer,
    TimeProvider timeProvider
) : SaveChangesInterceptor, IOpenIdSaveChangesInterceptor
{
    private IDirectoryOutboxPolicy Policy { get; } = policy;
    private IDirectoryOutboxNotifier Notifier { get; } = notifier;
    private IPersistedPayloadSerializer PayloadSerializer { get; } = payloadSerializer;
    private TimeProvider TimeProvider { get; } = timeProvider;

    // The rows that a save added or changed, per context, so the publisher is woken only after a save that wrote any,
    // and a failed save leaves nothing behind for the next attempt to add again.
    private ConditionalWeakTable<DbContext, List<Written>> Pending { get; } = new();

    #region Saving

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is OpenIdDbContext context && Policy.IsPublishing)
        {
            var touched = Collect(context);
            var heldSecrets = FindControlPlaneSecrets(context, touched.SecretIds);
            touched.Resolve(heldSecrets);
            foreach (var document in touched.Documents)
            {
                var rows = Load(context, document);
                Write(context, document, rows);
            }
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is OpenIdDbContext context && Policy.IsPublishing)
        {
            var touched = Collect(context);
            var heldSecrets = await FindControlPlaneSecretsAsync(
                context,
                touched.SecretIds,
                cancellationToken
            );
            touched.Resolve(heldSecrets);
            foreach (var document in touched.Documents)
            {
                var rows = await LoadAsync(context, document, cancellationToken);
                Write(context, document, rows);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    #endregion

    #region Saved

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyWhenWritten(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        NotifyWhenWritten(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Undo(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Undo(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void NotifyWhenWritten(DbContext? context)
    {
        if (context is not null && Pending.Remove(context))
        {
            Notifier.Notify();
        }
    }

    private void Undo(DbContext? context)
    {
        if (context is null || !Pending.TryGetValue(context, out var written))
        {
            return;
        }

        Pending.Remove(context);
        foreach (var item in written)
        {
            var entry = context.Entry(item.Entity);
            if (item.WasAdded)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
        }
    }

    #endregion

    #region Collect

    private Touched Collect(OpenIdDbContext context)
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        var touched = new Touched();

        var entries = context
            .ChangeTracker.Entries()
            .Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            )
            .ToList();

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case RoleAssignmentEntity assignment:
                    if (assignment.ResourceType == OpenIdResourceTypes.Platform)
                    {
                        touched.Touch(ReplicatedDocumentKinds.PlatformAuthority, string.Empty);
                    }
                    else if (assignment.ResourceType == OpenIdResourceTypes.Team)
                    {
                        touched.Touch(
                            ReplicatedDocumentKinds.TeamAuthority,
                            assignment.NormalizedResourceId
                        );
                    }

                    break;

                case ClientEntity client when client.NormalizedTenantId == controlPlane:
                    touched.Touch(ReplicatedDocumentKinds.PlatformAuthority, string.Empty);
                    break;

                case TeamEntity team when entry.State == EntityState.Added:
                    touched.Touch(ReplicatedDocumentKinds.TeamAuthority, team.NormalizedTeamId);
                    break;

                case TeamEntity team when entry.State == EntityState.Deleted:
                    touched.Retire(ReplicatedDocumentKinds.TeamAuthority, team.NormalizedTeamId);
                    break;

                case RetiredTeamEntity retired when entry.State == EntityState.Added:
                    touched.Retire(ReplicatedDocumentKinds.TeamAuthority, retired.NormalizedTeamId);
                    break;

                case TenantSecretEntity tenantSecret
                    when tenantSecret.NormalizedTenantId == controlPlane:
                    touched.Touch(ReplicatedDocumentKinds.ManagementKeys, string.Empty);
                    break;

                case RevokedManagementKeyEntity when entry.State == EntityState.Added:
                    touched.Touch(ReplicatedDocumentKinds.ManagementKeys, string.Empty);
                    break;

                case SecretEntity secret when entry.State != EntityState.Added:
                    touched.SecretIds.Add(secret.Id);
                    break;
            }
        }

        if (touched.Documents.Count > 0 || touched.SecretIds.Count > 0)
        {
            // Every row that composes a document belongs to the control-plane tenant or to an unscoped table, so the
            // reads below see all of them in an unscoped scope and in the control-plane tenant's scope. In another
            // tenant's scope row-level security would hide them and the composed content would silently be partial.
            var inControlPlaneScope = context.NormalizedAmbientTenantId == controlPlane;
            if (!context.IsUnscoped && !inControlPlaneScope)
            {
                throw new InvalidOperationException(
                    $"{nameof(ReplicatedDocumentOutboxInterceptor)} runs only in an unscoped scope or in the control-plane tenant's scope."
                );
            }
        }

        return touched;
    }

    private IQueryable<TenantSecretEntity> ControlPlaneSecretLinks(
        OpenIdDbContext context,
        IReadOnlyCollection<Guid> secretIds
    )
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        return context
            .Set<TenantSecretEntity>()
            .AsNoTracking()
            // Cross-tenant: the control-plane tenant's secrets are read whatever the ambient tenant is.
            .IgnoreQueryFilters()
            .Where(link =>
                link.NormalizedTenantId == controlPlane && secretIds.Contains(link.SecretId)
            );
    }

    private bool FindControlPlaneSecrets(OpenIdDbContext context, HashSet<Guid> secretIds)
    {
        if (secretIds.Count == 0)
        {
            return false;
        }

        var query = ControlPlaneSecretLinks(context, secretIds);
        return query.Any();
    }

    private async ValueTask<bool> FindControlPlaneSecretsAsync(
        OpenIdDbContext context,
        HashSet<Guid> secretIds,
        CancellationToken cancellationToken
    )
    {
        if (secretIds.Count == 0)
        {
            return false;
        }

        var query = ControlPlaneSecretLinks(context, secretIds);
        return await query.AnyAsync(cancellationToken);
    }

    #endregion

    #region Load

    private Rows Load(OpenIdDbContext context, Document document)
    {
        var rows = new Rows();
        if (!document.IsRetired)
        {
            switch (document.Kind)
            {
                case ReplicatedDocumentKinds.PlatformAuthority:
                    rows.Assignments = PlatformAssignments(context).ToList();
                    rows.Clients = ControlPlaneClients(context).ToList();
                    break;
                case ReplicatedDocumentKinds.TeamAuthority:
                    rows.Assignments = TeamAssignments(context, document.Key).ToList();
                    break;
                case ReplicatedDocumentKinds.ManagementKeys:
                    rows.TenantSecrets = ControlPlaneSecrets(context).ToList();
                    rows.Revoked = RevokedKeys(context).ToList();
                    break;
            }
        }

        rows.Revision = FindRevision(context, document);
        return rows;
    }

    private async ValueTask<Rows> LoadAsync(
        OpenIdDbContext context,
        Document document,
        CancellationToken cancellationToken
    )
    {
        var rows = new Rows();
        if (!document.IsRetired)
        {
            switch (document.Kind)
            {
                case ReplicatedDocumentKinds.PlatformAuthority:
                    rows.Assignments = await PlatformAssignments(context)
                        .ToListAsync(cancellationToken);
                    rows.Clients = await ControlPlaneClients(context)
                        .ToListAsync(cancellationToken);
                    break;
                case ReplicatedDocumentKinds.TeamAuthority:
                    rows.Assignments = await TeamAssignments(context, document.Key)
                        .ToListAsync(cancellationToken);
                    break;
                case ReplicatedDocumentKinds.ManagementKeys:
                    rows.TenantSecrets = await ControlPlaneSecrets(context)
                        .ToListAsync(cancellationToken);
                    rows.Revoked = await RevokedKeys(context).ToListAsync(cancellationToken);
                    break;
            }
        }

        rows.Revision = await FindRevisionAsync(context, document, cancellationToken);
        return rows;
    }

    private static IQueryable<RoleAssignmentEntity> PlatformAssignments(OpenIdDbContext context) =>
        context
            .Set<RoleAssignmentEntity>()
            .AsNoTracking()
            // Cross-tenant: assignments at the platform node are stored under the tenant of their holder.
            .IgnoreQueryFilters()
            .Where(entity => entity.ResourceType == OpenIdResourceTypes.Platform);

    private static IQueryable<RoleAssignmentEntity> TeamAssignments(
        OpenIdDbContext context,
        string teamId
    ) =>
        context
            .Set<RoleAssignmentEntity>()
            .AsNoTracking()
            // Cross-tenant: assignments at a team's node are stored under the tenant of their holder.
            .IgnoreQueryFilters()
            .Where(entity =>
                entity.ResourceType == OpenIdResourceTypes.Team
                && entity.NormalizedResourceId == teamId
            );

    private IQueryable<ClientEntity> ControlPlaneClients(OpenIdDbContext context)
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        return context
            .Set<ClientEntity>()
            .AsNoTracking()
            // Cross-tenant: the control-plane tenant's clients are read whatever the ambient tenant is.
            .IgnoreQueryFilters()
            .Where(entity => entity.NormalizedTenantId == controlPlane);
    }

    private IQueryable<TenantSecretEntity> ControlPlaneSecrets(OpenIdDbContext context)
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        return context
            .Set<TenantSecretEntity>()
            .AsNoTracking()
            // Cross-tenant: the control-plane tenant's secrets are read whatever the ambient tenant is.
            .IgnoreQueryFilters()
            .Include(entity => entity.Secret)
            .Where(entity => entity.NormalizedTenantId == controlPlane);
    }

    private static IQueryable<RevokedManagementKeyEntity> RevokedKeys(OpenIdDbContext context) =>
        context.Set<RevokedManagementKeyEntity>().AsNoTracking();

    private static ReplicatedSourceRevisionEntity? FindLocalRevision(
        OpenIdDbContext context,
        Document document
    ) =>
        context
            .Set<ReplicatedSourceRevisionEntity>()
            .Local.FirstOrDefault(entity =>
                entity.Kind == document.Kind && entity.Key == document.Key
            );

    private static ReplicatedSourceRevisionEntity? FindRevision(
        OpenIdDbContext context,
        Document document
    )
    {
        var local = FindLocalRevision(context, document);
        if (local is not null)
        {
            return local;
        }

        return context
            .Set<ReplicatedSourceRevisionEntity>()
            .FirstOrDefault(entity => entity.Kind == document.Kind && entity.Key == document.Key);
    }

    private static async ValueTask<ReplicatedSourceRevisionEntity?> FindRevisionAsync(
        OpenIdDbContext context,
        Document document,
        CancellationToken cancellationToken
    )
    {
        var local = FindLocalRevision(context, document);
        if (local is not null)
        {
            return local;
        }

        return await context
            .Set<ReplicatedSourceRevisionEntity>()
            .FirstOrDefaultAsync(
                entity => entity.Kind == document.Kind && entity.Key == document.Key,
                cancellationToken
            );
    }

    #endregion

    #region Write

    private void Write(OpenIdDbContext context, Document document, Rows rows)
    {
        var content = document.IsRetired ? null : Compose(context, document, rows);

        var written = Pending.GetOrCreateValue(context);

        long revision;
        var token = Guid.NewGuid().ToString("N");
        if (rows.Revision is null)
        {
            revision = 1;
            var created = new ReplicatedSourceRevisionEntity
            {
                Kind = document.Kind,
                Key = document.Key,
                Revision = revision,
                ConcurrencyToken = token,
            };
            context.Set<ReplicatedSourceRevisionEntity>().Add(created);
            var createdItem = new Written(created, WasAdded: true);
            written.Add(createdItem);
        }
        else
        {
            revision = rows.Revision.Revision + 1;
            var entry = context.Entry(rows.Revision);
            entry.Property(entity => entity.Revision).CurrentValue = revision;
            entry.Property(entity => entity.ConcurrencyToken).CurrentValue = token;
            var updatedItem = new Written(rows.Revision, WasAdded: false);
            written.Add(updatedItem);
        }

        var outbox = new DirectoryOutboxEntity
        {
            Kind = document.Kind,
            Key = document.Key,
            Revision = revision,
            ContentJson = content,
            CreatedWhen = TimeProvider.GetUtcNow(),
        };
        context.Set<DirectoryOutboxEntity>().Add(outbox);
        var outboxItem = new Written(outbox, WasAdded: true);
        written.Add(outboxItem);
    }

    private string Compose(OpenIdDbContext context, Document document, Rows rows)
    {
        if (document.Kind == ReplicatedDocumentKinds.ManagementKeys)
        {
            var tenantSecrets = OverlayTenantSecrets(context, rows.TenantSecrets);
            var revoked = OverlayRevoked(context, rows.Revoked);
            return ReplicatedDocumentComposer.ComposeManagementKeys(
                PayloadSerializer,
                tenantSecrets,
                revoked
            );
        }

        var assignments = OverlayAssignments(context, document, rows.Assignments);
        if (document.Kind == ReplicatedDocumentKinds.TeamAuthority)
        {
            return ReplicatedDocumentComposer.ComposeTeamAuthority(PayloadSerializer, assignments);
        }

        var clients = OverlayClients(context, rows.Clients);
        return ReplicatedDocumentComposer.ComposePlatformAuthority(
            PayloadSerializer,
            assignments,
            clients
        );
    }

    private static List<RoleAssignmentEntity> OverlayAssignments(
        OpenIdDbContext context,
        Document document,
        List<RoleAssignmentEntity> stored
    )
    {
        var byId = stored.ToDictionary(entity => entity.Id);
        var resourceType =
            document.Kind == ReplicatedDocumentKinds.PlatformAuthority
                ? OpenIdResourceTypes.Platform
                : OpenIdResourceTypes.Team;

        foreach (var entry in context.ChangeTracker.Entries<RoleAssignmentEntity>())
        {
            var entity = entry.Entity;
            var inDocument =
                entity.ResourceType == resourceType
                && (
                    resourceType == OpenIdResourceTypes.Platform
                    || entity.NormalizedResourceId == document.Key
                );
            if (!inDocument)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Deleted:
                    byId.Remove(entity.Id);
                    break;
                case EntityState.Added or EntityState.Modified:
                    byId[entity.Id] = entity;
                    break;
            }
        }

        return byId.Values.ToList();
    }

    private List<ClientEntity> OverlayClients(OpenIdDbContext context, List<ClientEntity> stored)
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        var byId = stored.ToDictionary(entity => entity.Id);

        foreach (var entry in context.ChangeTracker.Entries<ClientEntity>())
        {
            var entity = entry.Entity;
            if (entity.NormalizedTenantId != controlPlane)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Deleted:
                    byId.Remove(entity.Id);
                    break;
                case EntityState.Added or EntityState.Modified:
                    byId[entity.Id] = entity;
                    break;
            }
        }

        return byId.Values.ToList();
    }

    private List<TenantSecretEntity> OverlayTenantSecrets(
        OpenIdDbContext context,
        List<TenantSecretEntity> stored
    )
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        var byId = stored.ToDictionary(entity => entity.Id);

        foreach (var entry in context.ChangeTracker.Entries<TenantSecretEntity>())
        {
            var entity = entry.Entity;
            if (entity.NormalizedTenantId != controlPlane)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Deleted:
                    byId.Remove(entity.Id);
                    break;
                case EntityState.Added or EntityState.Modified:
                    byId[entity.Id] = entity;
                    break;
            }
        }

        var changedSecrets = new Dictionary<Guid, EntityEntry<SecretEntity>>();
        foreach (var entry in context.ChangeTracker.Entries<SecretEntity>())
        {
            changedSecrets[entry.Entity.Id] = entry;
        }

        var result = new List<TenantSecretEntity>(byId.Count);
        foreach (var tenantSecret in byId.Values)
        {
            if (!changedSecrets.TryGetValue(tenantSecret.SecretId, out var secretEntry))
            {
                result.Add(tenantSecret);
                continue;
            }

            if (secretEntry.State == EntityState.Deleted)
            {
                continue;
            }

            if (secretEntry.State == EntityState.Modified)
            {
                var replaced = new TenantSecretEntity
                {
                    Id = tenantSecret.Id,
                    TenantId = tenantSecret.TenantId,
                    NormalizedTenantId = tenantSecret.NormalizedTenantId,
                    SecretId = tenantSecret.SecretId,
                    Tenant = tenantSecret.Tenant,
                    Secret = secretEntry.Entity,
                };
                result.Add(replaced);
                continue;
            }

            result.Add(tenantSecret);
        }

        return result;
    }

    private static List<RevokedManagementKeyEntity> OverlayRevoked(
        OpenIdDbContext context,
        List<RevokedManagementKeyEntity> stored
    )
    {
        var byKeyId = stored.ToDictionary(entity => entity.KeyId, StringComparer.Ordinal);
        foreach (var entry in context.ChangeTracker.Entries<RevokedManagementKeyEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                byKeyId[entry.Entity.KeyId] = entry.Entity;
            }
        }

        return byKeyId.Values.ToList();
    }

    #endregion

    #region Types

    private sealed record Document(string Kind, string Key, bool IsRetired);

    private sealed record Written(object Entity, bool WasAdded);

    private sealed class Rows
    {
        public List<RoleAssignmentEntity> Assignments { get; set; } = [];
        public List<ClientEntity> Clients { get; set; } = [];
        public List<TenantSecretEntity> TenantSecrets { get; set; } = [];
        public List<RevokedManagementKeyEntity> Revoked { get; set; } = [];
        public ReplicatedSourceRevisionEntity? Revision { get; set; }
    }

    private sealed class Touched
    {
        private Dictionary<(string Kind, string Key), bool> Map { get; } = new();

        public HashSet<Guid> SecretIds { get; } = [];

        public IReadOnlyList<Document> Documents =>
            Map.Select(pair => new Document(pair.Key.Kind, pair.Key.Key, pair.Value))
                .OrderBy(document => document.Kind, StringComparer.Ordinal)
                .ThenBy(document => document.Key, StringComparer.Ordinal)
                .ToList();

        public void Touch(string kind, string key)
        {
            Map.TryAdd((kind, key), false);
        }

        public void Retire(string kind, string key)
        {
            Map[(kind, key)] = true;
        }

        public void Resolve(bool controlPlaneSecretChanged)
        {
            if (controlPlaneSecretChanged)
            {
                Touch(ReplicatedDocumentKinds.ManagementKeys, string.Empty);
            }
        }
    }

    #endregion
}
