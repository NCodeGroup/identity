// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

/// <summary>
/// Provides a base implementation for <see cref="IStore"/> that uses Entity Framework Core for data persistence.
/// This abstract class provides common functionality for CRUD operations, entity mapping, and tenant management.
/// </summary>
/// <typeparam name="TItem">The type of the persisted item, also known as a <c>Data Transfer Object</c> (DTO),
/// which represents the data contract used outside the persistence layer.</typeparam>
/// <typeparam name="TEntity">The type of the corresponding Entity Framework entity used for database operations.</typeparam>
[PublicAPI]
public abstract class BaseStore<TItem, TEntity> : IStore
    where TItem : class
    where TEntity : class
{
    private static string CursorCollection { get; } = typeof(TEntity).Name;

    /// <summary>
    /// Gets the <see cref="IStoreProvider"/> instance that provides access to other stores
    /// and dependency injection services.
    /// </summary>
    protected abstract IStoreProvider StoreProvider { get; }

    /// <summary>
    /// Gets the <see cref="ISurrogateKeyGenerator"/> instance used to mint the surrogate keys of entities.
    /// </summary>
    protected abstract ISurrogateKeyGenerator KeyGenerator { get; }

    /// <summary>
    /// Gets the <see cref="IPageCursorProtector"/> instance that encrypts and authenticates the continuation cursors of
    /// this store.
    /// </summary>
    protected abstract IPageCursorProtector CursorProtector { get; }

    /// <summary>
    /// Gets the <see cref="OpenIdDbContext"/> instance used for database operations in this store.
    /// </summary>
    protected abstract OpenIdDbContext DbContext { get; }

    /// <summary>
    /// Gets the next surrogate key for an entity, minted by the <see cref="KeyGenerator"/>.
    /// </summary>
    /// <returns>A newly minted surrogate key.</returns>
    protected Guid NextId() => KeyGenerator.CreateKey();

    /// <summary>
    /// Generates a new unique concurrency token for optimistic concurrency control.
    /// The token is a GUID formatted as a 32-character hexadecimal string without hyphens.
    /// </summary>
    /// <returns>A new unique concurrency token string.</returns>
    protected static string NextConcurrencyToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Normalizes a string value to lowercase for case-insensitive database lookups.
    /// This enables sargable (Search ARGument ABLE) queries for DBMS engines that
    /// don't support case-insensitive indices natively.
    /// </summary>
    /// <param name="value">The string value to normalize, or <c>null</c>.</param>
    /// <returns>The lowercase version of the input string, or <c>null</c> if the input was <c>null</c>.</returns>
    [return: NotNullIfNotNull("value")]
    protected static string? Normalize(string? value) => value?.ToLowerInvariant();

    /// <summary>
    /// Maps an entity to its corresponding DTO.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the
    /// asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// newly mapped DTO instance.</returns>
    protected abstract ValueTask<TItem> MapFromEntityAsync(
        TEntity entity,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to retrieve an entity from the store using the provided predicate.
    /// </summary>
    /// <param name="predicate">The predicate to use to find the entity.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the entity if found; otherwise <c>null</c>.</returns>
    protected abstract ValueTask<TEntity?> GetEntityOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Returns an already-tracked entity matching the predicate without querying the database. The lookup applies the
    /// tenant scope the fail-closed tenant-scoping query filter applies, so it answers exactly what the query would: a
    /// tracked entity that is not tenant-bound matches; a tracked tenant-child entity matches only when the scope is
    /// unscoped or its tenant is the ambient tenant. With the scope unset, no tracked tenant-child entity matches. EF
    /// identity resolution would return this same tracked instance from a query anyway, so a <c>Local</c>-first lookup
    /// avoids a redundant round-trip when the entity was already loaded in the current unit of work.
    /// </summary>
    /// <param name="predicate">The predicate to match against the tracked (local) entities.</param>
    /// <returns>The tracked entity if one matches in the tenant scope; otherwise <c>null</c>.</returns>
    protected TEntity? GetLocalOrDefault(Expression<Func<TEntity, bool>> predicate)
    {
        var compiled = predicate.Compile();
        var isUnscoped = DbContext.IsUnscoped;
        var ambientTenantId = DbContext.NormalizedAmbientTenantId;
        return DbContext
            .Set<TEntity>()
            .Local.FirstOrDefault(entity =>
                compiled(entity) && IsInTenantScope(entity, isUnscoped, ambientTenantId)
            );
    }

    private static bool IsInTenantScope(TEntity entity, bool isUnscoped, string? ambientTenantId)
    {
        return isUnscoped
            || entity is not ISupportTenantEntity tenantEntity
            || (
                ambientTenantId is not null
                && string.Equals(
                    tenantEntity.NormalizedTenantId,
                    ambientTenantId,
                    StringComparison.Ordinal
                )
            );
    }

    /// <summary>
    /// Attempts to retrieve a DTO from the store using the provided predicate.
    /// </summary>
    /// <param name="predicate">The predicate to use to find the entity.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the DTO if found; otherwise <c>null</c>.</returns>
    protected virtual async ValueTask<TItem?> GetOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken
    )
    {
        var entity = await GetEntityOrDefaultAsync(predicate, cancellationToken);
        return entity is null ? null : await MapFromEntityAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Gets a single page of entities ordered by their surrogate identifier, starting after the specified value.
    /// </summary>
    /// <param name="afterId">The exclusive lower-bound surrogate identifier, or <c>null</c> to start at the beginning.</param>
    /// <param name="take">The maximum number of entities to return (the caller requests one extra to detect a next page).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page of entities.</returns>
    protected abstract ValueTask<IReadOnlyList<TEntity>> GetEntityPageAsync(
        Guid? afterId,
        int take,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the keyset sort key for the specified entity (its surrogate identifier).
    /// </summary>
    /// <param name="entity">The entity whose sort key is returned.</param>
    /// <returns>The sort key.</returns>
    protected abstract Guid GetSortKey(TEntity entity);

    /// <summary>
    /// Gets a single page of persisted items using keyset (cursor) pagination.
    /// </summary>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page and the
    /// cursor for the next page.</returns>
    public async ValueTask<PagedResult<TItem>> GetPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        var afterId = DecodeCursor(page.Cursor);

        // Fetch one extra row so a full page signals that another page exists.
        var entities = await GetEntityPageAsync(afterId, page.Limit + 1, cancellationToken);

        return await BuildPageAsync(entities, page.Limit, cancellationToken);
    }

    /// <summary>
    /// Builds a <see cref="PagedResult{TItem}"/> from a keyset fetch of <c>limit + 1</c> entities: maps the first
    /// <paramref name="limit"/> items and, when a further row was returned, encodes the next-page cursor.
    /// </summary>
    /// <param name="entities">The entities returned by a keyset fetch (ordered by sort key, up to <c>limit + 1</c>).</param>
    /// <param name="limit">The maximum number of items on the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page and the
    /// cursor for the next page.</returns>
    protected async ValueTask<PagedResult<TItem>> BuildPageAsync(
        IReadOnlyList<TEntity> entities,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var hasMore = entities.Count > limit;
        var count = hasMore ? limit : entities.Count;

        var items = new List<TItem>(count);
        for (var index = 0; index < count; index++)
        {
            var item = await MapFromEntityAsync(entities[index], cancellationToken);
            items.Add(item);
        }

        string? nextCursor = null;
        if (hasMore)
        {
            var sortKey = GetSortKey(entities[count - 1]);
            nextCursor = EncodeCursor(sortKey);
        }

        return new PagedResult<TItem> { Items = items, NextCursor = nextCursor };
    }

    /// <summary>
    /// Encodes a keyset sort key into an opaque continuation cursor: the key protected by the
    /// <see cref="CursorProtector"/>, so the cursor is encrypted and authenticated.
    /// </summary>
    /// <param name="key">The sort key of the last item on the page.</param>
    /// <returns>The opaque continuation cursor.</returns>
    protected string EncodeCursor(Guid key) => CursorProtector.ProtectKey(CursorCollection, key);

    /// <summary>
    /// Decodes an opaque continuation cursor into its keyset sort key. A malformed, tampered or undecryptable cursor
    /// decodes to <c>null</c>, which starts the list rather than raising a hard error.
    /// </summary>
    /// <param name="cursor">The opaque cursor, or <c>null</c> to start at the beginning.</param>
    /// <returns>The exclusive lower-bound sort key, or <c>null</c> to start at the beginning.</returns>
    protected Guid? DecodeCursor(string? cursor) =>
        CursorProtector.UnprotectKey(CursorCollection, cursor);

    #region IStoreProvider

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public virtual TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    #endregion
}
