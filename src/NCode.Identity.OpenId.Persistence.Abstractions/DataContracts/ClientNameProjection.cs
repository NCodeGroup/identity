// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

// See ADR-1080.
/// <summary>
/// Contains a lightweight read projection of a client's principal and client identifier, without its settings, secrets
/// or resource server, for a reader that names the client's principal.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ClientNameProjection
{
    /// <summary>
    /// Gets the identifier of the client's principal.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.PrincipalId)]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the identifier of the client.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string ClientId { get; init; }
}
