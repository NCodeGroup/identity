// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Capabilities;

/// <summary>
/// Describes the cell that serves the management API: its name, its region and whether it is the control-plane cell.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CellCapabilityResource
{
    /// <summary>
    /// Gets the name of the cell.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the region of the cell.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets a value indicating whether the cell is the control-plane cell, which serves the platform and team
    /// management routes.
    /// </summary>
    public required bool IsControlPlane { get; init; }
}
