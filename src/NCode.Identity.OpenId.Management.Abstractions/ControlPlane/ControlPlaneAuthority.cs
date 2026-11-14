// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The roles the control plane holds for a caller at the platform and at a team: the answer to a
/// <see cref="ControlPlaneAuthorityRequest"/>.
/// </summary>
[PublicAPI]
public sealed class ControlPlaneAuthority
{
    /// <summary>
    /// Gets the answer for a caller that holds no role.
    /// </summary>
    public static ControlPlaneAuthority None { get; } = new();

    /// <summary>
    /// Gets the names of the roles the caller holds at the platform node, distinct and ordered ordinally.
    /// </summary>
    public IReadOnlyList<string> PlatformRoles { get; init; } = [];

    /// <summary>
    /// Gets the names of the roles the caller holds at the requested team and at every team above it, distinct and
    /// ordered ordinally. The list is empty when the request names no team.
    /// </summary>
    public IReadOnlyList<string> TeamRoles { get; init; } = [];

    /// <summary>
    /// Gets a value that indicates whether the caller holds the <see cref="BuiltInRoles.GlobalAdmin"/> role at the
    /// platform node.
    /// </summary>
    public bool IsGlobalAdmin { get; init; }

    /// <summary>
    /// Gets the control-plane principal identifier that the subject resolves to, or <see langword="null"/> when it
    /// resolves to none. Role assignments in every cell hold this identifier.
    /// </summary>
    public string? PrincipalId { get; init; }
}
