// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// The options document of a connection of the <c>local</c> strategy.
/// </summary>
// See ADR-1535.
[PublicAPI]
public sealed class LocalConnectionOptions
{
    /// <summary>
    /// Gets the name of the local-account store that holds the connection's accounts.
    /// </summary>
    [JsonPropertyName("store")]
    public required string Store { get; init; }
}
