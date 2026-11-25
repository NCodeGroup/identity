// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Describes the body of a cell registration: the cell's region and kind and its client key set. The cell's name is the
/// route's.
/// </summary>
internal sealed record CellRegistrationBody
{
    /// <summary>
    /// Gets the region that the cell runs in.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the kind of the cell.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Gets the JSON Web Key Set that the cell's client assertions are verified with.
    /// </summary>
    public JsonElement Jwks { get; init; }
}
