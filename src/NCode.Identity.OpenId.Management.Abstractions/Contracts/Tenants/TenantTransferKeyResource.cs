// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the REST resource returned when a cell issues a one-time transfer key to receive a tenant. It carries
/// the public key only.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantTransferKeyResource
{
    /// <summary>
    /// Gets the <c>kid</c> that names the transfer key.
    /// </summary>
    public required string Kid { get; init; }

    /// <summary>
    /// Gets the public key as a <c>JSON Web Key (JWK)</c> for the <c>ECDH-ES+A256KW</c> algorithm.
    /// </summary>
    public required JsonElement Jwk { get; init; }
}
