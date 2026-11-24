// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

// See ADR-1080.
/// <summary>
/// Contains a lightweight read projection of the names of a federated identity (its principal's identifier, its
/// connection, its issuer and its subject), without its join key or tenant, for a reader that names the identity's
/// principal.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class FederatedIdentityNameProjection
{
    /// <summary>
    /// Gets the identifier of the principal that owns the federated identity.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.PrincipalId)]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the identifier of the connection that authenticated the federated identity.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets the upstream issuer, when the connection is external, recorded for audit and display. It is not part of any
    /// key. This value is <c>null</c> when the connection has no upstream issuer.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.Issuer)]
    public required string? Issuer { get; init; }

    /// <summary>
    /// Gets the subject of the federated identity.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.SubjectId)]
    public required string Subject { get; init; }
}
