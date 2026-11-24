// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the registration that the control-plane cell makes for a cell: the cell's name, its region and kind, and
/// the JSON Web Key Set whose private keys the cell authenticates to the directory's token endpoint with.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record CellRegistration
{
    /// <summary>
    /// Gets the name of the cell.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the region the cell runs in.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets the kind of the cell. <see cref="CellKinds"/> names the known kinds.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the JSON Web Key Set that holds the public keys the cell's client assertions are verified with.
    /// </summary>
    public required JsonElement Jwks { get; init; }
}
