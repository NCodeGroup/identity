// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the REST resource returned when a cell imports a tenant that another cell exported.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantImportResource
{
    /// <summary>
    /// Gets the identifier of the imported tenant, which stays disabled.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the name of the cell that held the tenant and exported it.
    /// </summary>
    public required string SourceCell { get; init; }
}
