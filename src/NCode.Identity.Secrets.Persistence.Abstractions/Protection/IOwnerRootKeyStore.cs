// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedOwnerRootKey"/> instances.
/// </summary>
/// <remarks>
/// An owner root key is not scoped to a tenant, so a read names its owner or owners, its owner type, or the due keys.
/// <see cref="IStore{T}.UpdateAsync"/> throws a <see cref="StoreConcurrencyException"/> when the concurrency token does
/// not match, and an <see cref="InvalidOperationException"/> when the row is gone.
/// </remarks>
// See ADR-1414.
[PublicAPI]
public interface IOwnerRootKeyStore : IStore<PersistedOwnerRootKey>
{
    /// <summary>
    /// Gets the <see cref="PersistedOwnerRootKey"/> of the specified owner. The owner is matched without regard to
    /// case.
    /// </summary>
    /// <param name="owner">The <see cref="SecretOwner"/> whose root key to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the root key, or
    /// <see langword="null"/> when the owner has none.</returns>
    ValueTask<PersistedOwnerRootKey?> FindAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the <see cref="PersistedOwnerRootKey"/> instances of the specified owners of one owner type in one read,
    /// ordered by an opaque row identifier. An owner that has no root key is skipped. The owner type and the owners are
    /// matched without regard to case.
    /// </summary>
    /// <param name="ownerType">The type of the owners, such as one of the values in
    /// <see cref="SecretOwnerTypes"/>.</param>
    /// <param name="ownerIds">The identifiers of the owners whose root keys to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the root keys of the
    /// owners that have one, or an empty list when none has or <paramref name="ownerIds"/> is empty.</returns>
    ValueTask<IReadOnlyList<PersistedOwnerRootKey>> FindManyAsync(
        string ownerType,
        IReadOnlyCollection<string> ownerIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a page of the owner root keys that are due for a probe, ordered by an opaque row identifier. A key is due
    /// when it is <see cref="OwnerRootKeyStates.Active"/> and was last validated before <paramref name="validatedBefore"/>
    /// or never, or when it is <see cref="OwnerRootKeyStates.Unavailable"/> or <see cref="OwnerRootKeyStates.Removing"/>
    /// and last failed before <paramref name="retryBefore"/> or never.
    /// </summary>
    /// <param name="validatedBefore">The instant before which an active key's last validation makes it due.</param>
    /// <param name="retryBefore">The instant before which an unavailable or removing key's last failure makes it due.</param>
    /// <param name="page">The <see cref="PageRequest"/> that selects the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page.</returns>
    ValueTask<PagedResult<PersistedOwnerRootKey>> GetDuePageAsync(
        DateTimeOffset validatedBefore,
        DateTimeOffset retryBefore,
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a page of the owner root keys of one owner type, ordered by an opaque row identifier so that the pages are
    /// stable while rows are added or changed. The owner type is matched without regard to case.
    /// </summary>
    /// <param name="ownerType">The type of the owners whose root keys to page, such as one of the values in
    /// <see cref="SecretOwnerTypes"/>.</param>
    /// <param name="page">The <see cref="PageRequest"/> that selects the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page.</returns>
    ValueTask<PagedResult<PersistedOwnerRootKey>> GetOwnerTypePageAsync(
        string ownerType,
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a <see cref="PersistedOwnerRootKey"/> from the store.
    /// </summary>
    /// <param name="item">The <see cref="PersistedOwnerRootKey"/> to remove, carrying the concurrency token it was
    /// read with.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when the root key is removed, or <see langword="false"/> when it is not in the store.</returns>
    /// <exception cref="StoreConcurrencyException">
    /// Thrown when the root key changed since the item was read.
    /// </exception>
    ValueTask<bool> RemoveAsync(PersistedOwnerRootKey item, CancellationToken cancellationToken);
}
