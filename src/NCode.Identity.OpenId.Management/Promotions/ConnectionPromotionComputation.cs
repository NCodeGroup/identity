// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides the outcome of computing the connections a promotion carries from the source tenant to the target tenant.
/// </summary>
// See ADR-1535.
internal sealed class ConnectionPromotionComputation
{
    /// <summary>
    /// Gets the promotion of each source connection.
    /// </summary>
    public List<ConnectionPromotion> Promotions { get; } = [];
}
