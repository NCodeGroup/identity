// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Represents the public half of a one-time transfer key that a cell issued to receive a tenant. The private half stays
/// in the cell and is taken once through <see cref="ITenantTransferKeyService.TakeAsync"/>.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantTransferKey
{
    /// <summary>
    /// Gets the <c>kid</c> that names the transfer key.
    /// </summary>
    public required string KeyId { get; init; }

    /// <summary>
    /// Gets the public key as a <c>JSON Web Key (JWK)</c> for the <c>ECDH-ES+A256KW</c> algorithm. It carries no
    /// private members.
    /// </summary>
    public required JsonElement Jwk { get; init; }
}
