// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides extension methods for <see cref="IPlatformTopology"/>.
/// </summary>
[PublicAPI]
public static class PlatformTopologyExtensions
{
    /// <summary>
    /// Determines whether the cell is the control-plane cell of a platform with a directory, which is dedicated to the
    /// control-plane tenant, so a tenant that is created, created on demand or imported in it is refused.
    /// </summary>
    /// <param name="platformTopology">The topology of the platform that the cell belongs to.</param>
    /// <returns><c>true</c> when the cell holds only the control-plane tenant; otherwise, <c>false</c>.</returns>
    public static bool HoldsOnlyControlPlaneTenant(this IPlatformTopology platformTopology) =>
        platformTopology.HasDirectory && platformTopology.IsControlPlaneCell;
}
