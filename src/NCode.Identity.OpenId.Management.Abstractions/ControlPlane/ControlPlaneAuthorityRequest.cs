// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The question a cell asks the control plane: which roles does a caller hold at the platform, and at a team and the
/// teams above it.
/// </summary>
[PublicAPI]
public sealed class ControlPlaneAuthorityRequest
{
    /// <summary>
    /// Gets the subject (<c>sub</c>) of the caller's access token.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the identifier (<c>client_id</c>) of the client the caller acts as.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets the identifier of the team whose roles are asked for, or <see langword="null"/> to ask for the platform's
    /// alone.
    /// </summary>
    public string? TeamId { get; init; }
}
