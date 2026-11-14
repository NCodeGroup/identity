// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Directory;

/// <summary>
/// Describes a cell as the directory lists it.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record CellDescriptor
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
    /// Gets the absolute URL at which the cell's management API is reachable.
    /// </summary>
    public required string ManagementUrl { get; init; }

    /// <summary>
    /// Gets a value indicating whether the cell is the control-plane cell.
    /// </summary>
    public bool IsControlPlane { get; init; }
}
