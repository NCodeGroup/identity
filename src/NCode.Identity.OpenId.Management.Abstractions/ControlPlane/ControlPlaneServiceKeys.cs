// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Defines the service keys under which a host registers the remote adapters of the control plane, so the management
/// library chooses between its local adapters and the remote ones from the cell's options.
/// </summary>
[PublicAPI]
public static class ControlPlaneServiceKeys
{
    /// <summary>
    /// The key of the remote <see cref="IManagementTokenValidator"/> and the remote
    /// <see cref="IControlPlaneAuthority"/>. A cell that is not the control plane resolves them when they are
    /// registered, and the control-plane cell never does.
    /// </summary>
    public const string Remote = "remote";
}
