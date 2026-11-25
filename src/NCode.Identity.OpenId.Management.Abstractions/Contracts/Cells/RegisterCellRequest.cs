// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Cells;

/// <summary>
/// Represents the request that registers a cell with the global directory: the cell's region and kind, and the JSON Web
/// Key Set whose private keys the cell authenticates to the directory's token endpoint with. The cell's name is the
/// route's.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class RegisterCellRequest
{
    /// <summary>
    /// Gets the region the cell runs in.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets the kind of the cell, such as <c>shared</c> or <c>dedicated</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the JSON Web Key Set that holds the public keys the cell's client assertions are verified with.
    /// </summary>
    public required JsonElement Jwks { get; init; }
}
