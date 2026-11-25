// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one role held by a principal at a node.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityAssignment
{
    /// <summary>
    /// Gets the identifier of the principal.
    /// </summary>
    [JsonPropertyName("principalId")]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the name of the role.
    /// </summary>
    [JsonPropertyName("roleName")]
    public required string RoleName { get; init; }
}
