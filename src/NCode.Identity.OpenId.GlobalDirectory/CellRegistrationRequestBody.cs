// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the body of a cell registration request, which names the cell by its route.
/// </summary>
internal sealed record CellRegistrationRequestBody
{
    /// <summary>
    /// Gets the region that the cell runs in.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets the kind of the cell.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the JSON Web Key Set that publishes the cell's public keys.
    /// </summary>
    public required System.Text.Json.JsonElement Jwks { get; init; }
}
