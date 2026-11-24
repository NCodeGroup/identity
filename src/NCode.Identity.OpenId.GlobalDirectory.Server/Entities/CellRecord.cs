// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for a cell that registered itself with the directory.
/// </summary>
public sealed class CellRecord
{
    /// <summary>
    /// Gets or sets the name of the cell, which is its identity.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the region that the cell runs in.
    /// </summary>
    public required string Region { get; set; }

    /// <summary>
    /// Gets or sets the kind of the cell, one of the <see cref="CellKinds"/> values.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// Gets or sets the URL of the cell's management API, or <c>null</c> until the cell reports its descriptor.
    /// </summary>
    public string? ManagementUrl { get; set; }

    /// <summary>
    /// Gets or sets the JSON Web Key Set that holds the public keys the cell's client assertions are verified with, or
    /// <c>null</c> when the cell is not registered.
    /// </summary>
    public string? ClientJwksJson { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cell is retired. A retired cell's name is never reissued.
    /// </summary>
    public bool IsRetired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cell is the control-plane cell.
    /// </summary>
    public bool IsControlPlane { get; set; }
}
