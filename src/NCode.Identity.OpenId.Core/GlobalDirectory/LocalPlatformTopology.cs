// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IPlatformTopology"/> of a platform of one cell, which has no global directory and no
/// management tenant of its own.
/// </summary>
internal sealed class LocalPlatformTopology(IOptions<CellOptions> cellOptions) : IPlatformTopology
{
    /// <inheritdoc />
    public bool HasDirectory => false;

    /// <inheritdoc />
    public bool IsControlPlaneCell => cellOptions.Value.IsControlPlane;

    /// <inheritdoc />
    public bool HasManagementTenant => false;
}
