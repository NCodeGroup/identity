// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the request body to export a disabled tenant to the cell that receives it.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantExportRequest
{
    /// <summary>
    /// Gets the public half of the receiving cell's transfer key as a <c>JSON Web Key (JWK)</c>: an elliptic curve key
    /// on <c>P-256</c> for encryption. The export is sealed to it, so only the cell that holds the private half reads
    /// the export.
    /// </summary>
    public required JsonElement Jwk { get; init; }

    /// <summary>
    /// Gets the name of the cell that receives the tenant. The exporting cell offers the tenant to it in the global
    /// directory and writes the name into the package, so only that cell imports the package. The name must be a valid
    /// cell label.
    /// </summary>
    public required string TargetCell { get; init; }
}
