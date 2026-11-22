// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Delivers the audit records that an audit stream has not yet received to the stream's endpoint, while the replica
/// holds the stream's lease.
/// </summary>
// See ADR-1534.
internal interface IAuditStreamDeliverer
{
    /// <summary>
    /// Delivers batches of the stream's records until none are left, the lease is half spent, or a delivery fails, and
    /// then releases the lease.
    /// </summary>
    /// <param name="state">The delivery state of a stream whose lease the replica holds.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask DeliverAsync(PersistedAuditStreamState state, CancellationToken cancellationToken);
}
