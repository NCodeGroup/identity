// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for an issuer that a tenant gave up, by changing its issuer or by being
/// deleted. Relying parties that trusted the issuer would trust any tenant that took it, so only the tenant that held it
/// can take it back.
/// </summary>
public sealed class RetiredIssuerRecord
{
    /// <summary>
    /// Gets the surrogate identifier for this record.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets or sets the retired issuer.
    /// </summary>
    public required string Issuer { get; set; }

    /// <summary>
    /// Gets or sets the value of <see cref="Issuer"/> normalized so that lookups are case-insensitive.
    /// </summary>
    public required string NormalizedIssuer { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the tenant that retired the issuer.
    /// </summary>
    public required string TenantId { get; set; }
}
