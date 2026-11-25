// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the roles that a principal holds according to the replicated authority documents.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ReplicatedAuthority
{
    /// <summary>
    /// Gets the names of the roles held at the platform node, distinct and ordered ordinally.
    /// </summary>
    public IReadOnlyList<string> PlatformRoles { get; init; } = [];

    /// <summary>
    /// Gets the names of the roles held at the requested team and at every team above it, distinct and ordered
    /// ordinally. It is empty when no team was requested.
    /// </summary>
    public IReadOnlyList<string> TeamRoles { get; init; } = [];

    /// <summary>
    /// Gets the identifier of the control-plane client that the principal acts as, or <c>null</c> when the principal is
    /// not an enabled client of the control-plane tenant.
    /// </summary>
    public string? ClientId { get; init; }
}
