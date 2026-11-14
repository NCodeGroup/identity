// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Cells;

/// <summary>
/// Represents one cell of the platform as the control-plane cell lists it. It carries no management URL.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CellResource
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
    /// Gets the kind of the cell, such as <c>shared</c> or <c>dedicated</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets a value indicating whether the cell is the control-plane cell.
    /// </summary>
    public required bool IsControlPlane { get; init; }
}
