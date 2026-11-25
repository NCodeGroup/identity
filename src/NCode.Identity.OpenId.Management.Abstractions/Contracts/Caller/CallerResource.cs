// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Caller;

/// <summary>
/// Represents where the caller of the management API holds authority.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CallerResource
{
    /// <summary>
    /// Gets the identifier of the caller's principal, or <see langword="null"/> when the caller resolves to no principal.
    /// </summary>
    public string? PrincipalId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the caller holds authority at the platform node.
    /// </summary>
    public required bool IsPlatformAdministrator { get; init; }

    /// <summary>
    /// Gets the role assignments held by the caller's principal.
    /// </summary>
    public required IReadOnlyList<RoleAssignmentResource> Assignments { get; init; }

    /// <summary>
    /// Gets the roles the caller holds at the node of the cell that serves the request, including <c>GlobalAdmin</c>
    /// when the platform authority grants it. It is empty outside a workload cell.
    /// </summary>
    public required IReadOnlyList<string> CellRoles { get; init; }
}
