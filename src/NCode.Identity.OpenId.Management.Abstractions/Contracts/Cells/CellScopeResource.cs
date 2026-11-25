// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Cells;

/// <summary>
/// Represents the cell that serves the management request, as the cell describes itself.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CellScopeResource
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
    /// Gets a value indicating whether the cell has a management tenant of its own, whose principals administer the
    /// cell.
    /// </summary>
    public required bool HasManagementTenant { get; init; }
}
