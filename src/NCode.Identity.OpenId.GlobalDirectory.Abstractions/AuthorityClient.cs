// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one enabled client of the control-plane tenant.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityClient
{
    /// <summary>
    /// Gets the identifier of the principal that the client acts as.
    /// </summary>
    [JsonPropertyName("principalId")]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the client identifier.
    /// </summary>
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }
}
