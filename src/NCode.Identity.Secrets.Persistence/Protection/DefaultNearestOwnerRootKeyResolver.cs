// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Provides the default implementation of <see cref="INearestOwnerRootKeyResolver"/>.
/// </summary>
/// <remarks>
/// The owner's own root key is the nearest one when it is owner-rooted. Otherwise the ancestors that
/// <see cref="ISecretOwnerParentResolver"/> names are read nearest first, and the first root key that is owner-rooted
/// is the nearest one.
/// </remarks>
/// <param name="parentResolver">The resolver that names the ancestors of an owner.</param>
// See ADR-1414.
internal sealed class DefaultNearestOwnerRootKeyResolver(ISecretOwnerParentResolver parentResolver)
    : INearestOwnerRootKeyResolver
{
    private ISecretOwnerParentResolver ParentResolver { get; } = parentResolver;

    /// <inheritdoc />
    public async ValueTask<PersistedOwnerRootKey?> FindNearestAsync(
        IOwnerRootKeyStore store,
        SecretOwner owner,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(store);

        var key = owner.Normalize();

        var own = await store.FindAsync(key, cancellationToken);
        if (MasterKeyFactory.IsOwnerRooted(own))
            return own;

        var ancestors = await ParentResolver.GetAncestorsAsync(key, cancellationToken);
        foreach (var ancestor in ancestors)
        {
            var normalizedAncestor = ancestor.Normalize();
            var row = await store.FindAsync(normalizedAncestor, cancellationToken);
            if (MasterKeyFactory.IsOwnerRooted(row))
                return row;
        }

        return null;
    }
}
