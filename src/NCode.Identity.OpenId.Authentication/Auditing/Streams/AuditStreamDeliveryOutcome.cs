// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// The outcome of the batches a replica delivered while it held the lease.
/// </summary>
/// <param name="Error">The error that stopped the batches, or <see langword="null"/> when they ended with the records,
/// the batch size or the lease.</param>
/// <param name="HoldsLease"><see langword="false"/> when a save found the lease gone.</param>
// See ADR-1534.
internal readonly record struct AuditStreamDeliveryOutcome(string? Error, bool HoldsLease);
