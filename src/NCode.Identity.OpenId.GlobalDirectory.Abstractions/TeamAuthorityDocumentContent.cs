// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the content of a team authority document: the role assignments at one team's node.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TeamAuthorityDocumentContent
{
    /// <summary>
    /// Gets the role assignments at the team's node.
    /// </summary>
    [JsonPropertyName("assignments")]
    public required IReadOnlyList<AuthorityAssignment> Assignments { get; init; }
}
