// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts;

/// <summary>
/// Represents the control plane's summary of a principal: what a caller shows in place of its opaque identifier.
/// </summary>
// See ADR-1465.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PrincipalSummaryResource
{
    /// <summary>
    /// Gets the opaque identifier of the principal.
    /// </summary>
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the type of the principal, a <c>PrincipalTypes</c> value.
    /// </summary>
    public required string PrincipalType { get; init; }

    /// <summary>
    /// Gets the name to show for the principal.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the username of the principal, or <see langword="null"/> for a principal without one.
    /// </summary>
    public string? UserName { get; init; }
}
