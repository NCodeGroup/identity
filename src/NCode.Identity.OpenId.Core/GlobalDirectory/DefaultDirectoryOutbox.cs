// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IDirectoryOutbox"/>. It maps the outbox store, which is not
/// tenant-scoped, and reads and writes it in an unscoped ambient tenant scope.
/// </summary>
// See ADR-1536.
internal sealed class DefaultDirectoryOutbox(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IDirectoryOutbox
{
    /// <summary>
    /// The reason that the outbox gives when it opens an unscoped ambient tenant scope.
    /// </summary>
    internal const string UnscopedReason = "directory outbox";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReplicatedDocument>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryOutboxStore>();

        var entries = await store.ListPendingAsync(limit, cancellationToken);
        var documents = new List<ReplicatedDocument>(entries.Count);
        foreach (var entry in entries)
        {
            JsonElement? content = null;
            if (entry.ContentJson is not null)
            {
                content = JsonElement.Parse(entry.ContentJson);
            }

            var document = new ReplicatedDocument
            {
                Kind = entry.Kind,
                Key = entry.Key,
                Revision = entry.Revision,
                Content = content,
            };
            documents.Add(document);
        }

        return documents;
    }

    /// <inheritdoc />
    public async ValueTask AcknowledgeAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryOutboxStore>();

        await store.RemoveThroughAsync(kind, key, revision, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReplicatedDocument>> ListLocalRevisionsAsync(
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryOutboxStore>();

        var revisions = await store.ListSourceRevisionsAsync(cancellationToken);
        return revisions
            .Select(revision => new ReplicatedDocument
            {
                Kind = revision.Kind,
                Key = revision.Key,
                Revision = revision.Revision,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask RepublishAsync(
        string kind,
        string key,
        long atRevision,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryOutboxStore>();

        await store.EnqueueCurrentAsync(kind, key, atRevision, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }
}
