// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedAuditStream"/>: the endpoints that receive a
/// tenant's audit records as they are stored, with the secrets that sign the deliveries and each stream's delivery
/// state. The methods that take no tenant work in the ambient tenant scope.
/// </summary>
// See ADR-1534.
[PublicAPI]
public interface IAuditStreamStore : IStore
{
    /// <summary>
    /// Adds a stream, when the unit of work is saved, with its signing secrets and a delivery state positioned at the
    /// stream's creation time and <see cref="Guid.Empty"/>, due at once.
    /// </summary>
    /// <param name="stream">The stream to add.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask AddAsync(PersistedAuditStream stream, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a stream of the ambient tenant, with its signing secrets and delivery state.
    /// </summary>
    /// <param name="streamId">The identifier of the stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the stream, or
    /// <see langword="null"/> when the tenant has no such stream.</returns>
    ValueTask<PersistedAuditStream?> GetOrDefaultAsync(
        string streamId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets one page of the ambient tenant's streams, ordered by their identifier, using keyset (cursor) pagination,
    /// with each stream's signing secrets and delivery state.
    /// </summary>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page and the
    /// cursor for the next page.</returns>
    ValueTask<PagedResult<PersistedAuditStream>> GetPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Counts the ambient tenant's streams.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the count of
    /// streams.</returns>
    ValueTask<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates, when the unit of work is saved, a stream's display name, endpoint, enabled flag and set of signing
    /// secrets, checked against the stream's concurrency token.
    /// </summary>
    /// <param name="stream">The stream to update, carrying the concurrency token it was read with.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="StoreConcurrencyException">Thrown when the stream was modified since it was read.</exception>
    ValueTask UpdateAsync(PersistedAuditStream stream, CancellationToken cancellationToken);

    /// <summary>
    /// Removes, when the unit of work is saved, a stream of the ambient tenant with its delivery state and signing
    /// secrets.
    /// </summary>
    /// <param name="streamId">The identifier of the stream.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when the stream existed, or <see langword="false"/> when it did not.</returns>
    ValueTask<bool> RemoveAsync(string streamId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the delivery states that are due, of every tenant, for a caller that opened an unscoped scope: the states
    /// of enabled streams whose next attempt time has come, whose lease is free or expired and whose tenant has a
    /// record ready to send, ordered by their next attempt time.
    /// </summary>
    /// <param name="now">The time to compare the next attempt times and the lease expiry times with.</param>
    /// <param name="storedBefore">The time before which a record must be stored to be ready: a stream is due only when
    /// its tenant has a record stored after the stream's position and before this time.</param>
    /// <param name="limit">The greatest number of states to return.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the due
    /// states.</returns>
    ValueTask<IReadOnlyList<PersistedAuditStreamState>> GetDueStatesAsync(
        DateTimeOffset now,
        DateTimeOffset storedBefore,
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Writes, when the unit of work is saved, a delivery state, checked against the state's concurrency token, and the
    /// save gives the stored state a fresh token, so a caller that writes the state again reads it first.
    /// </summary>
    /// <param name="state">The state to write, carrying the concurrency token it was read with.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="StoreConcurrencyException">Thrown when the state was modified since it was read, or is
    /// gone.</exception>
    ValueTask UpdateStateAsync(
        PersistedAuditStreamState state,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a single page of the protected signing secrets of the streams of a tenant, ordered by their identifier,
    /// using keyset (cursor) pagination. It reads in the ambient tenant scope, which the caller opens for the tenant.
    /// Each value's <see cref="PersistedProtectedValue.RecordId"/> is the stream and its
    /// <see cref="PersistedProtectedValue.ItemId"/> is the secret.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant whose streams are read. An unknown tenant has none.</param>
    /// <param name="valuePrefixes">The texts that a protected value must start with to be returned, so that only the
    /// values under the requested master keys leave the database. They must all have the same length.</param>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page and the
    /// cursor for the next page.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="valuePrefixes"/> is empty or holds prefixes of
    /// different lengths.</exception>
    ValueTask<PagedResult<PersistedProtectedValue>> GetProtectedSigningSecretPageAsync(
        string tenantId,
        IReadOnlyCollection<string> valuePrefixes,
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Replaces the protected value of a signing secret, leaving every other member as it is. It finds the stream in
    /// the ambient tenant scope.
    /// </summary>
    /// <param name="value">The value to write, carrying the concurrency token it was read with.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when the value was written, or <see langword="false"/> when the stream or the secret does
    /// not exist.</returns>
    /// <exception cref="StoreConcurrencyException">Thrown when the stream was modified since the value was
    /// read.</exception>
    ValueTask<bool> UpdateProtectedSigningSecretAsync(
        PersistedProtectedValue value,
        CancellationToken cancellationToken
    );
}
