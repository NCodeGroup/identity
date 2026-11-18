// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the request body that the cell holding a tenant sends to offer the tenant to another cell through
/// <see cref="ITenantDirectory.OfferMoveAsync"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantMoveOfferRequest
{
    /// <summary>
    /// Gets the name of the cell that the tenant is offered to.
    /// </summary>
    public required string ToCell { get; init; }
}
