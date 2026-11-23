// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The question a cell asks the control plane: which control-plane principals do these identifiers and usernames name.
/// </summary>
[PublicAPI]
public sealed class ControlPlanePrincipalQuery
{
    /// <summary>
    /// The most principal identifiers and usernames, together, that one query names.
    /// </summary>
    public const int MaxNames = 100;

    /// <summary>
    /// Gets the identifiers of the principals to describe.
    /// </summary>
    public IReadOnlyList<string> PrincipalIds { get; init; } = [];

    /// <summary>
    /// Gets the usernames of the principals to describe.
    /// </summary>
    public IReadOnlyList<string> UserNames { get; init; } = [];
}
