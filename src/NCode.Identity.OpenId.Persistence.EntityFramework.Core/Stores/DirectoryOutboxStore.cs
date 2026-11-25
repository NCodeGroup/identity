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
            .OrderBy(entity => entity.Kind)
            .ThenBy(entity => entity.Key)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => new PersistedSourceRevision
            {
                Kind = entity.Kind,
                Key = entity.Key,
                Revision = entity.Revision,
            })
            .ToList();
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
                return ReplicatedDocumentComposer.ComposeAuthority(
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
                return ReplicatedDocumentComposer.ComposeAuthority(
                    PayloadSerializer,
                    teamAssignments,
                    []
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
