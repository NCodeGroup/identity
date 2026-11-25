// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the content of an authority document: the role assignments at one node and, for the platform document, the
/// control-plane tenant's clients.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityDocumentContent
{
    /// <summary>
    /// Gets the role assignments at the node.
    /// </summary>
    [JsonPropertyName("assignments")]
    public required IReadOnlyList<AuthorityAssignment> Assignments { get; init; }

    /// <summary>
    /// Gets the enabled clients of the control-plane tenant, which the platform document lists so that a cell knows
    /// offline whether a token's subject is a client acting for itself. It is empty in a team document.
    /// </summary>
    [JsonPropertyName("clients")]
    public IReadOnlyList<AuthorityClient> Clients { get; init; } = [];
}
