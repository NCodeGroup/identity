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
    /// Gets or sets the URL of the cell's management API.
    /// </summary>
    public required string ManagementUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cell is the control-plane cell.
    /// </summary>
    public bool IsControlPlane { get; set; }
}
