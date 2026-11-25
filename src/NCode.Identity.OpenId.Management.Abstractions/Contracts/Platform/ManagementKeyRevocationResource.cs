// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Platform;

/// <summary>
/// Represents the REST resource returned by the revocation of a management key: the key that left the control-plane
/// tenant's key set and the key that signs in its place.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ManagementKeyRevocationResource
{
    /// <summary>
    /// Gets the key identifier (<c>kid</c>) of the revoked key.
    /// </summary>
    public required string RevokedKeyId { get; init; }

    /// <summary>
    /// Gets the key identifier (<c>kid</c>) of the key that signs in place of the revoked one.
    /// </summary>
    public required string ReplacementKeyId { get; init; }
}
