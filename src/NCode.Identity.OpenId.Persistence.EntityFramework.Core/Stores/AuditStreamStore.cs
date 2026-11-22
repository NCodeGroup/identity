// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IAuditStreamStore"/> that uses Entity Framework Core for
/// persistence. A stream's signing secrets and delivery state are rows of their own, and every foreign key restricts
/// deletion, so a removal stages the children with the stream. A stream and its state have separate concurrency tokens,
/// so a delivery writing the state never conflicts with the management of the stream.
/// </summary>
// See ADR-1534.
internal sealed class AuditStreamStore(
    IStoreProvider storeProvider,
    ISurrogateKeyGenerator keyGenerator,
    IPageCursorProtector cursorProtector,
    OpenIdDbContext openIdDbContext
) : CoreBaseStore<PersistedAuditStream, AuditStreamEntity>, IAuditStreamStore
{
    /// <inheritdoc />
    protected override IStoreProvider StoreProvider { get; } = storeProvider;

    /// <inheritdoc />
    protected override ISurrogateKeyGenerator KeyGenerator { get; } = keyGenerator;

    /// <inheritdoc />
    protected override IPageCursorProtector CursorProtector { get; } = cursorProtector;

    /// <inheritdoc />
    protected override OpenIdDbContext DbContext { get; } = openIdDbContext;

    private IQueryable<AuditStreamEntity> StreamsWithChildren =>
        DbContext
            .AuditStreams.Include(entity => entity.Tenant)
            .Include(entity => entity.State)
            .Include(entity => entity.SigningSecrets);

    private static PersistedAuditStreamState MapState(
        AuditStreamStateEntity entity,
        string tenantId,
        string streamId
    ) =>
        new()
        {
            TenantId = tenantId,
            StreamId = streamId,
            PositionStoredTime = entity.PositionStoredTime,
            PositionEventId = entity.PositionEventId,
            NextAttemptTime = entity.NextAttemptTime,
            ConsecutiveFailures = entity.ConsecutiveFailures,
            LastDeliveryTime = entity.LastDeliveryTime,
            LastFailureTime = entity.LastFailureTime,
            LastError = entity.LastError,
            LeaseHolder = entity.LeaseHolder,
            LeaseExpiryTime = entity.LeaseExpiryTime,
            ConcurrencyToken = entity.ConcurrencyToken,
        };

    /// <inheritdoc />
    protected override ValueTask<PersistedAuditStream> MapFromEntityAsync(
        AuditStreamEntity entity,
        CancellationToken cancellationToken
    )
    {
        var tenantId = entity.Tenant.TenantId;
        var persistedAuditStream = new PersistedAuditStream
        {
            TenantId = tenantId,
            StreamId = entity.StreamId,
            DisplayName = entity.DisplayName,
            Endpoint = entity.Endpoint,
            IsEnabled = entity.IsEnabled,
            CreatedTime = entity.CreatedTime,
            ConcurrencyToken = entity.ConcurrencyToken,
            SigningSecrets = entity
                .SigningSecrets.OrderBy(secret => secret.CreatedTime)
                .ThenBy(secret => secret.SecretId, StringComparer.Ordinal)
                .Select(secret => new PersistedAuditStreamSigningSecret
                {
                    SecretId = secret.SecretId,
                    ProtectedValue = secret.ProtectedValue,
                    CreatedTime = secret.CreatedTime,
                    ExpiryTime = secret.ExpiryTime,
                })
                .ToList(),
            State = entity.State is null ? null : MapState(entity.State, tenantId, entity.StreamId),
        };
        return ValueTask.FromResult(persistedAuditStream);
    }

    /// <inheritdoc />
    protected override async ValueTask<AuditStreamEntity?> GetEntityOrDefaultAsync(
        Expression<Func<AuditStreamEntity, bool>> predicate,
        CancellationToken cancellationToken
    )
    {
        return GetLocalOrDefault(predicate)
            ?? await StreamsWithChildren.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<AuditStreamEntity>> GetEntityPageAsync(
        Guid? afterId,
        int take,
        CancellationToken cancellationToken
    )
    {
        var query = StreamsWithChildren;

        if (afterId is { } id)
        {
            query = query.Where(entity => entity.Id > id);
        }

        return await query.OrderBy(entity => entity.Id).Take(take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override Guid GetSortKey(AuditStreamEntity entity) => entity.Id;

    /// <inheritdoc />
    public async ValueTask<PersistedAuditStream?> GetOrDefaultAsync(
        string streamId,
        CancellationToken cancellationToken
    )
    {
        return await GetOrDefaultAsync(entity => entity.StreamId == streamId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> CountAsync(CancellationToken cancellationToken)
    {
        return await DbContext.AuditStreams.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(
        PersistedAuditStream stream,
        CancellationToken cancellationToken
    )
    {
        var tenantEntity = await GetTenantEntityAsync(stream.TenantId, cancellationToken);
        var createdTime = stream.CreatedTime.ToUniversalTime();

        // The interceptor leaves a pre-seeded insert token intact (ADR-1012).
        stream.ConcurrencyToken = NextConcurrencyToken();

        var signingSecrets = new List<AuditStreamSigningSecretEntity>(stream.SigningSecrets.Count);
        var streamEntity = new AuditStreamEntity
        {
            Id = NextId(),
            TenantId = tenantEntity.Id,
            NormalizedTenantId = tenantEntity.NormalizedTenantId,
            StreamId = stream.StreamId,
            DisplayName = stream.DisplayName,
            Endpoint = stream.Endpoint,
            IsEnabled = stream.IsEnabled,
            CreatedTime = createdTime,
            ConcurrencyToken = stream.ConcurrencyToken,
            Tenant = tenantEntity,
            SigningSecrets = signingSecrets,
        };
        await DbContext.AuditStreams.AddAsync(streamEntity, cancellationToken);

        foreach (var signingSecret in stream.SigningSecrets)
        {
            var secretEntity = CreateSigningSecretEntity(streamEntity, signingSecret);
            signingSecrets.Add(secretEntity);
            await DbContext.AuditStreamSigningSecrets.AddAsync(secretEntity, cancellationToken);
        }

        // The stream's first delivery is due at once and reads the records stored since its creation.
        var stateEntity = new AuditStreamStateEntity
        {
            Id = NextId(),
            TenantId = tenantEntity.Id,
            NormalizedTenantId = tenantEntity.NormalizedTenantId,
            AuditStreamId = streamEntity.Id,
            PositionStoredTime = createdTime,
            PositionEventId = Guid.Empty,
            NextAttemptTime = createdTime,
            ConsecutiveFailures = 0,
            ConcurrencyToken = NextConcurrencyToken(),
            Tenant = tenantEntity,
            AuditStream = streamEntity,
        };
        await DbContext.AuditStreamStates.AddAsync(stateEntity, cancellationToken);
    }

    private AuditStreamSigningSecretEntity CreateSigningSecretEntity(
        AuditStreamEntity streamEntity,
        PersistedAuditStreamSigningSecret signingSecret
    ) =>
        new()
        {
            Id = NextId(),
            TenantId = streamEntity.TenantId,
            NormalizedTenantId = streamEntity.NormalizedTenantId,
            AuditStreamId = streamEntity.Id,
            SecretId = signingSecret.SecretId,
            ProtectedValue = signingSecret.ProtectedValue,
            CreatedTime = signingSecret.CreatedTime.ToUniversalTime(),
            ExpiryTime = signingSecret.ExpiryTime?.ToUniversalTime(),
            ConcurrencyToken = NextConcurrencyToken(),
            Tenant = streamEntity.Tenant,
            AuditStream = streamEntity,
        };

    /// <inheritdoc />
    public async ValueTask UpdateAsync(
        PersistedAuditStream stream,
        CancellationToken cancellationToken
    )
    {
        var streamEntity =
            await GetEntityOrDefaultAsync(
                entity => entity.StreamId == stream.StreamId,
                cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"The audit stream with StreamId='{stream.StreamId}' was not found."
            );

        if (
            !string.Equals(
                stream.ConcurrencyToken,
                streamEntity.ConcurrencyToken,
                StringComparison.Ordinal
            )
        )
        {
            throw new StoreConcurrencyException(
                "The audit stream has been modified by another process. Please reload and try again."
            );
        }

        streamEntity.DisplayName = stream.DisplayName;
        streamEntity.Endpoint = stream.Endpoint;
        streamEntity.IsEnabled = stream.IsEnabled;

        // The stream's token changes with its set of secrets too; marking the token modified makes the interceptor
        // regenerate it on save (ADR-1012), while the state row is left alone.
        DbContext.Entry(streamEntity).Property(entity => entity.ConcurrencyToken).IsModified = true;

        var existing = streamEntity.SigningSecrets.ToDictionary(
            secret => secret.SecretId,
            StringComparer.Ordinal
        );
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signingSecret in stream.SigningSecrets)
        {
            kept.Add(signingSecret.SecretId);

            if (existing.TryGetValue(signingSecret.SecretId, out var secretEntity))
            {
                secretEntity.ProtectedValue = signingSecret.ProtectedValue;
                secretEntity.ExpiryTime = signingSecret.ExpiryTime?.ToUniversalTime();
                continue;
            }

            var newSecretEntity = CreateSigningSecretEntity(streamEntity, signingSecret);
            await DbContext.AuditStreamSigningSecrets.AddAsync(newSecretEntity, cancellationToken);
        }

        var removed = existing.Values.Where(secret => !kept.Contains(secret.SecretId)).ToList();
        DbContext.AuditStreamSigningSecrets.RemoveRange(removed);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamEntity = await GetEntityOrDefaultAsync(
            entity => entity.StreamId == streamId,
            cancellationToken
        );
        if (streamEntity is null)
        {
            return false;
        }

        // Every foreign key restricts deletion, so the children are removed explicitly.
        DbContext.AuditStreamSigningSecrets.RemoveRange(streamEntity.SigningSecrets);
        if (streamEntity.State is { } stateEntity)
        {
            DbContext.AuditStreamStates.Remove(stateEntity);
        }

        DbContext.AuditStreams.Remove(streamEntity);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedAuditStreamState>> GetDueStatesAsync(
        DateTimeOffset now,
        DateTimeOffset storedBefore,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var utcNow = now.ToUniversalTime();
        var utcStoredBefore = storedBefore.ToUniversalTime();
        var auditRecords = DbContext.AuditRecords;

        // No query filter is ignored: the caller opened an unscoped scope, which matches every tenant.
        // A stream is due only when its tenant has a record after the stream's position, in the order the stream page
        // reads (ADR-1534), so an idle stream is never due and costs no lease, unwrap or poll slot.
        var entities = await DbContext
            .AuditStreamStates.Include(entity => entity.Tenant)
            .Include(entity => entity.AuditStream)
            .Where(entity =>
                entity.AuditStream.IsEnabled
                && entity.NextAttemptTime <= utcNow
                && (entity.LeaseExpiryTime == null || entity.LeaseExpiryTime <= utcNow)
                && auditRecords.Any(record =>
                    record.TenantId == entity.Tenant.TenantId
                    && record.StoredTime < utcStoredBefore
                    && (
                        record.StoredTime > entity.PositionStoredTime
                        || (
                            record.StoredTime == entity.PositionStoredTime
                            && record.EventId > entity.PositionEventId
                        )
                    )
                )
            )
            .OrderBy(entity => entity.NextAttemptTime)
            .ThenBy(entity => entity.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => MapState(entity, entity.Tenant.TenantId, entity.AuditStream.StreamId))
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask UpdateStateAsync(
        PersistedAuditStreamState state,
        CancellationToken cancellationToken
    )
    {
        var normalizedTenantId = Normalize(state.TenantId);
        var streamId = state.StreamId;
        var stateEntity = await DbContext
            .AuditStreamStates.Where(entity =>
                entity.NormalizedTenantId == normalizedTenantId
                && entity.AuditStream.StreamId == streamId
            )
            .SingleOrDefaultAsync(cancellationToken);

        if (
            stateEntity is null
            || !string.Equals(
                state.ConcurrencyToken,
                stateEntity.ConcurrencyToken,
                StringComparison.Ordinal
            )
        )
        {
            throw new StoreConcurrencyException(
                "The audit stream's delivery state has been modified by another process, or is gone. Please reload and try again."
            );
        }

        stateEntity.PositionStoredTime = state.PositionStoredTime.ToUniversalTime();
        stateEntity.PositionEventId = state.PositionEventId;
        stateEntity.NextAttemptTime = state.NextAttemptTime.ToUniversalTime();
        stateEntity.ConsecutiveFailures = state.ConsecutiveFailures;
        stateEntity.LastDeliveryTime = state.LastDeliveryTime?.ToUniversalTime();
        stateEntity.LastFailureTime = state.LastFailureTime?.ToUniversalTime();
        stateEntity.LastError = state.LastError;
        stateEntity.LeaseHolder = state.LeaseHolder;
        stateEntity.LeaseExpiryTime = state.LeaseExpiryTime?.ToUniversalTime();

        // The row's token is regenerated by the interceptor on save (ADR-1012), also when no field changed, so the
        // token on the given state is stale once the write is saved.
        DbContext.Entry(stateEntity).Property(entity => entity.ConcurrencyToken).IsModified = true;
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<PersistedProtectedValue>> GetProtectedSigningSecretPageAsync(
        string tenantId,
        IReadOnlyCollection<string> valuePrefixes,
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        var (prefixes, length) = ProtectedValuePrefixes.Validate(
            valuePrefixes,
            nameof(valuePrefixes)
        );

        var normalizedTenantId = Normalize(tenantId);
        var tenantKey = await DbContext
            .Tenants.Where(tenant => tenant.NormalizedTenantId == normalizedTenantId)
            .Select(tenant => (Guid?)tenant.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantKey is not { } tenantRowId)
            return new PagedResult<PersistedProtectedValue> { Items = [], NextCursor = null };

        var query = DbContext
            .AuditStreamSigningSecrets.AsNoTracking()
            .Where(entity => entity.TenantId == tenantRowId)
            // Only the rows under the requested keys leave the database (ADR-1407). The length guard keeps a provider
            // that evaluates Substring in memory from throwing on a shorter value.
            .Where(entity =>
                entity.ProtectedValue.Length >= length
                && prefixes.Contains(entity.ProtectedValue.Substring(0, length))
            );

        var afterId = DecodeCursor(page.Cursor);
        if (afterId is { } id)
            query = query.Where(entity => entity.Id > id);

        // Fetch one extra row so a full page signals that another page exists.
        var entities = await query
            .OrderBy(entity => entity.Id)
            .Take(page.Limit + 1)
            .Select(entity => new
            {
                entity.Id,
                entity.AuditStream.StreamId,
                entity.SecretId,
                entity.ProtectedValue,
                entity.AuditStream.ConcurrencyToken,
            })
            .ToListAsync(cancellationToken);

        var hasMore = entities.Count > page.Limit;
        var count = hasMore ? page.Limit : entities.Count;

        return new PagedResult<PersistedProtectedValue>
        {
            Items = entities
                .Take(count)
                .Select(entity => new PersistedProtectedValue
                {
                    RecordId = entity.StreamId,
                    ItemId = entity.SecretId,
                    ProtectedValue = entity.ProtectedValue,
                    ConcurrencyToken = entity.ConcurrencyToken,
                })
                .ToList(),
            NextCursor = hasMore ? EncodeCursor(entities[count - 1].Id) : null,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpdateProtectedSigningSecretAsync(
        PersistedProtectedValue value,
        CancellationToken cancellationToken
    )
    {
        var streamId = value.RecordId;
        var streamEntity = await DbContext
            .AuditStreams.Include(entity => entity.SigningSecrets)
            .Where(entity => entity.StreamId == streamId)
            .SingleOrDefaultAsync(cancellationToken);
        if (streamEntity is null)
            return false;

        if (
            !string.Equals(
                value.ConcurrencyToken,
                streamEntity.ConcurrencyToken,
                StringComparison.Ordinal
            )
        )
        {
            throw new StoreConcurrencyException(
                "The audit stream has been modified by another process. Please reload and try again."
            );
        }

        var secretEntity = streamEntity.SigningSecrets.SingleOrDefault(secret =>
            string.Equals(secret.SecretId, value.ItemId, StringComparison.Ordinal)
        );
        if (secretEntity is null)
            return false;

        // Touch the rows so the interceptor regenerates their ConcurrencyTokens on save (ADR-1012).
        secretEntity.ProtectedValue = value.ProtectedValue;
        DbContext.Entry(secretEntity).Property(entity => entity.ConcurrencyToken).IsModified = true;
        DbContext.Entry(streamEntity).Property(entity => entity.ConcurrencyToken).IsModified = true;
        return true;
    }
}
