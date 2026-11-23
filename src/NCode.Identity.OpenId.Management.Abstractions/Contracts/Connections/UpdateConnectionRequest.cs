// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Connections;

/// <summary>
/// Represents the mutable metadata of a connection that a JSON Patch document may modify. The name and the strategy
/// are immutable.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class UpdateConnectionRequest
{
    /// <summary>
    /// Gets or sets the human-readable name that the sign-in picker shows.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection is enabled.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the JSON object of strategy-specific options.
    /// </summary>
    public required JsonElement Options { get; set; }
}
