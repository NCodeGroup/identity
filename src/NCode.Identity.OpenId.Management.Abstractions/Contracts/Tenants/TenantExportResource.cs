// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the REST resource returned when a cell exports a disabled tenant. It carries the sealed export only.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantExportResource
{
    /// <summary>
    /// Gets the export as a compact <c>JWE</c> to the receiving cell's transfer key (<c>ECDH-ES+A256KW</c> with
    /// <c>A256GCM</c>). Its payload holds the cell's region, the tenant's identifier, the tenant's records by section
    /// and the tenant's master keys sealed to the same key.
    /// </summary>
    public required string Package { get; init; }
}
