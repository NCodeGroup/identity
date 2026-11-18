// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the request body to import a tenant that another cell exported, at the cell that receives it.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantImportRequest
{
    /// <summary>
    /// Gets the <c>kid</c> of the transfer key that this cell issued and that the export is sealed to. The cell takes
    /// the key once, so the same <c>kid</c> imports at most one package.
    /// </summary>
    public required string Kid { get; init; }

    /// <summary>
    /// Gets the export as a compact <c>JWE</c> that <see cref="TenantExportResource.Package"/> carried.
    /// </summary>
    public required string Package { get; init; }
}
