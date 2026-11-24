// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Connections;

/// <summary>
/// Represents the request body to create a new connection.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CreateConnectionRequest
{
    /// <summary>
    /// Gets the name of the connection, unique within its tenant.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-readable name that the sign-in picker shows.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the name of the registered strategy the connection uses.
    /// </summary>
    public required string Strategy { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connection is created enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the JSON object of strategy-specific options, or <c>null</c> for an empty object.
    /// </summary>
    public JsonElement? Options { get; init; }

    /// <summary>
    /// Gets the initial JSON settings for the connection, or <c>null</c> for none.
    /// </summary>
    public JsonElement? Settings { get; init; }
}
