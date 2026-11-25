// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IReplicatedDocumentApplier"/>. It stores a document as the cell's
/// replica only when it is newer than the replica, and records the key identifiers that a management keys document
/// revokes in the same unit of work, so no later document returns a revoked key.
/// </summary>
// See ADR-1536.
internal sealed class DefaultReplicatedDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IPersistedPayloadSerializer payloadSerializer,
    TimeProvider timeProvider
) : IReplicatedDocumentApplier
{
    /// <summary>
    /// The reason that the applier gives when it opens an unscoped ambient tenant scope to write the replicas.
    /// </summary>
    internal const string UnscopedReason = "directory replicated document";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IPersistedPayloadSerializer PayloadSerializer { get; } = payloadSerializer;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        ReplicatedDocument document,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        string? contentJson = null;
        if (document.Content is { ValueKind: not JsonValueKind.Null } content)
        {
            contentJson = content.GetRawText();
        }

        var revokedKeyIds = GetRevokedKeyIds(document.Kind, contentJson);
        var persistedDocument = new PersistedReplicatedDocument
        {
            Kind = document.Kind,
            Key = document.Key,
            Revision = document.Revision,
            ContentJson = contentJson,
        };

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IReplicatedDocumentStore>();

        var isApplied = await store.TryApplyAsync(persistedDocument, cancellationToken);
        if (!isApplied)
            return;

        var revokedWhen = TimeProvider.GetUtcNow();
        foreach (var keyId in revokedKeyIds)
        {
            var revokedKey = new PersistedRevokedManagementKey
            {
                KeyId = keyId,
                RevokedWhen = revokedWhen,
                Source = PersistedRevokedManagementKey.ControlPlaneSource,
            };
            await store.AddRevokedKeyAsync(revokedKey, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the identifiers of the keys that a management-keys document revokes.
    /// </summary>
    /// <param name="kind">The document kind.</param>
    /// <param name="contentJson">The document's content, when it has any.</param>
    /// <returns>The revoked key identifiers, or an empty list for any other kind of document.</returns>
    private IReadOnlyList<string> GetRevokedKeyIds(string kind, string? contentJson)
    {
        if (
            contentJson is null
            || !string.Equals(
                kind,
                ReplicatedDocumentKinds.ManagementKeys,
                StringComparison.Ordinal
            )
        )
        {
            return [];
        }

        var contentElement = JsonElement.Parse(contentJson);
        var keysContent = PayloadSerializer.Deserialize<ManagementKeysDocumentContent>(
            contentElement,
            JsonOptions
        );
        return keysContent.RevokedKeyIds;
    }
}
