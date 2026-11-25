// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IDirectoryOutboxStore"/> that uses Entity Framework Core for
/// persistence.
/// </summary>
/// <remarks>
/// This store does not save changes. The caller's store manager commits them.
/// </remarks>
internal class DirectoryOutboxStore(
    IStoreProvider storeProvider,
    OpenIdDbContext openIdDbContext,
    IDirectoryOutboxPolicy policy,
    IPersistedPayloadSerializer payloadSerializer,
    TimeProvider timeProvider
) : IDirectoryOutboxStore
{
    private IStoreProvider StoreProvider { get; } = storeProvider;

    private OpenIdDbContext DbContext { get; } = openIdDbContext;

    private IDirectoryOutboxPolicy Policy { get; } = policy;

    private IPersistedPayloadSerializer PayloadSerializer { get; } = payloadSerializer;

    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedDirectoryOutboxEntry>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .DirectoryOutbox.AsNoTracking()
            .OrderBy(entity => entity.Kind)
            .ThenBy(entity => entity.Key)
            .ThenBy(entity => entity.Revision)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => new PersistedDirectoryOutboxEntry
            {
                Sequence = entity.Sequence,
                Kind = entity.Kind,
                Key = entity.Key,
                Revision = entity.Revision,
                ContentJson = entity.ContentJson,
                CreatedWhen = entity.CreatedWhen,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask RemoveThroughAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .DirectoryOutbox.Where(entity =>
                entity.Kind == kind && entity.Key == key && entity.Revision <= revision
            )
            .ToListAsync(cancellationToken);

        DbContext.DirectoryOutbox.RemoveRange(entities);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedSourceRevision>> ListSourceRevisionsAsync(
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .ReplicatedSourceRevisions.AsNoTracking()
            .ToListAsync(cancellationToken);

        var revisions = new Dictionary<(string Kind, string Key), long>();
        foreach (var entity in entities)
        {
            revisions[(entity.Kind, entity.Key)] = entity.Revision;
        }

        // A document whose rows exist and that no source revision names has never been published: its rows were
        // written while the cell did not publish, so the publisher republishes it from its current rows (ADR-1536).
        var sources = await ListSourceDocumentsAsync(cancellationToken);
        foreach (var source in sources)
        {
            revisions.TryAdd(source, 0);
        }

        return revisions
            .OrderBy(pair => pair.Key.Kind, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Key, StringComparer.Ordinal)
            .Select(pair => new PersistedSourceRevision
            {
                Kind = pair.Key.Kind,
                Key = pair.Key.Key,
                Revision = pair.Value,
            })
            .ToList();
    }

    /// <summary>
    /// Lists the documents whose source rows exist: the platform authority when the platform node holds an assignment
    /// or the control-plane tenant a client, the management keys when the control-plane tenant holds a key or a key is
    /// revoked, and the authority of every team. These are the rows whose changes
    /// <see cref="ReplicatedDocumentOutboxInterceptor"/> publishes, and <see cref="ComposeCurrentAsync"/> composes each
    /// document from the same rows.
    /// </summary>
    private async ValueTask<IReadOnlyList<(string Kind, string Key)>> ListSourceDocumentsAsync(
        CancellationToken cancellationToken
    )
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();
        var documents = new List<(string Kind, string Key)>();

        var hasPlatformAssignment = await DbContext
            .Set<RoleAssignmentEntity>()
            // Cross-tenant: assignments at the platform node are stored under the tenant of their holder.
            .IgnoreQueryFilters()
            .AnyAsync(
                entity => entity.ResourceType == OpenIdResourceTypes.Platform,
                cancellationToken
            );
        var hasPlatformAuthority =
            hasPlatformAssignment
            || await DbContext
                .Set<ClientEntity>()
                // Cross-tenant: the control-plane tenant's clients are read whatever the ambient tenant is.
                .IgnoreQueryFilters()
                .AnyAsync(entity => entity.NormalizedTenantId == controlPlane, cancellationToken);
        if (hasPlatformAuthority)
        {
            documents.Add((ReplicatedDocumentKinds.PlatformAuthority, string.Empty));
        }

        var hasManagementKey =
            await DbContext
                .Set<TenantSecretEntity>()
                // Cross-tenant: the control-plane tenant's secrets are read whatever the ambient tenant is.
                .IgnoreQueryFilters()
                .AnyAsync(entity => entity.NormalizedTenantId == controlPlane, cancellationToken)
            || await DbContext.Set<RevokedManagementKeyEntity>().AnyAsync(cancellationToken);
        if (hasManagementKey)
        {
            documents.Add((ReplicatedDocumentKinds.ManagementKeys, string.Empty));
        }

        var teamIds = await DbContext
            .Set<TeamEntity>()
            .AsNoTracking()
            .Select(entity => entity.NormalizedTeamId)
            .ToListAsync(cancellationToken);
        foreach (var teamId in teamIds)
        {
            documents.Add((ReplicatedDocumentKinds.TeamAuthority, teamId));
        }

        return documents;
    }

    /// <inheritdoc />
    public async ValueTask EnqueueCurrentAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var content = await ComposeCurrentAsync(kind, key, cancellationToken);

        var sourceRevision = await DbContext.ReplicatedSourceRevisions.FirstOrDefaultAsync(
            entity => entity.Kind == kind && entity.Key == key,
            cancellationToken
        );

        var token = Guid.NewGuid().ToString("N");
        if (sourceRevision is null)
        {
            var created = new ReplicatedSourceRevisionEntity
            {
                Kind = kind,
                Key = key,
                Revision = revision,
                ConcurrencyToken = token,
            };
            DbContext.ReplicatedSourceRevisions.Add(created);
        }
        else
        {
            sourceRevision.Revision = revision;
            sourceRevision.ConcurrencyToken = token;
        }

        var outbox = new DirectoryOutboxEntity
        {
            Kind = kind,
            Key = key,
            Revision = revision,
            ContentJson = content,
            CreatedWhen = TimeProvider.GetUtcNow(),
        };
        DbContext.DirectoryOutbox.Add(outbox);
    }

    private async ValueTask<string?> ComposeCurrentAsync(
        string kind,
        string key,
        CancellationToken cancellationToken
    )
    {
        var controlPlane = Policy.ControlPlaneTenantId.ToLowerInvariant();

        switch (kind)
        {
            case ReplicatedDocumentKinds.PlatformAuthority:
                var platformAssignments = await DbContext
                    .Set<RoleAssignmentEntity>()
                    .AsNoTracking()
                    // Cross-tenant: assignments at the platform node are stored under the tenant of their holder.
                    .IgnoreQueryFilters()
                    .Where(entity => entity.ResourceType == OpenIdResourceTypes.Platform)
                    .ToListAsync(cancellationToken);
                var clients = await DbContext
                    .Set<ClientEntity>()
                    .AsNoTracking()
                    // Cross-tenant: the control-plane tenant's clients are read whatever the ambient tenant is.
                    .IgnoreQueryFilters()
                    .Where(entity => entity.NormalizedTenantId == controlPlane)
                    .ToListAsync(cancellationToken);
                return ReplicatedDocumentComposer.ComposePlatformAuthority(
                    PayloadSerializer,
                    platformAssignments,
                    clients
                );

            case ReplicatedDocumentKinds.TeamAuthority:
                var hasTeam = await DbContext
                    .Set<TeamEntity>()
                    .AnyAsync(entity => entity.NormalizedTeamId == key, cancellationToken);
                if (!hasTeam)
                {
                    return null;
                }

                var teamAssignments = await DbContext
                    .Set<RoleAssignmentEntity>()
                    .AsNoTracking()
                    // Cross-tenant: assignments at a team's node are stored under the tenant of their holder.
                    .IgnoreQueryFilters()
                    .Where(entity =>
                        entity.ResourceType == OpenIdResourceTypes.Team
                        && entity.NormalizedResourceId == key
                    )
                    .ToListAsync(cancellationToken);
                return ReplicatedDocumentComposer.ComposeTeamAuthority(
                    PayloadSerializer,
                    teamAssignments
                );

            case ReplicatedDocumentKinds.ManagementKeys:
                var tenantSecrets = await DbContext
                    .Set<TenantSecretEntity>()
                    .AsNoTracking()
                    // Cross-tenant: the control-plane tenant's secrets are read whatever the ambient tenant is.
                    .IgnoreQueryFilters()
                    .Include(entity => entity.Secret)
                    .Where(entity => entity.NormalizedTenantId == controlPlane)
                    .ToListAsync(cancellationToken);
                var revoked = await DbContext
                    .Set<RevokedManagementKeyEntity>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                return ReplicatedDocumentComposer.ComposeManagementKeys(
                    PayloadSerializer,
                    tenantSecrets,
                    revoked
                );

            default:
                throw new ArgumentException(
                    $"The replicated document kind '{kind}' is not known.",
                    nameof(kind)
                );
        }
    }
}
