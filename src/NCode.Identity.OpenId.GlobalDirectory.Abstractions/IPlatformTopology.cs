// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the shape of the platform a cell belongs to: whether a global directory exists, whether the cell is the
/// dedicated control-plane cell and whether the cell has a management tenant of its own. Rules that apply only on a
/// platform with a directory read it. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IPlatformTopology
{
    /// <summary>
    /// Gets a value indicating whether the platform has a global directory, which the cell reaches through
    /// <c>Directory:Url</c>.
    /// </summary>
    bool HasDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether the cell is the control-plane cell.
    /// </summary>
    bool IsControlPlaneCell { get; }

    /// <summary>
    /// Gets a value indicating whether the cell has a management tenant of its own, which it has only on a platform
    /// with a directory, when it is not the control-plane cell and it serves a dynamic tenant-resolution strategy
    /// (<c>DynamicByHost</c> or <c>DynamicByPath</c>), which every workload cell of such a platform serves. A platform
    /// of one cell has none, because its one cell serves the control-plane tenant.
    /// </summary>
    bool HasManagementTenant { get; }
}
