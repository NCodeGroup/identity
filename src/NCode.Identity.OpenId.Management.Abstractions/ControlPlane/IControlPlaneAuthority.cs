// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Answers which platform and team roles a caller holds. The control plane holds the platform and team role
/// assignments, so a cell reads them from its own store when it is the control plane, and from the control plane
/// through a remote adapter otherwise.
/// </summary>
[PublicAPI]
public interface IControlPlaneAuthority
{
    /// <summary>
    /// Gets the platform and team roles that a caller holds.
    /// </summary>
    /// <param name="request">The <see cref="ControlPlaneAuthorityRequest"/> that names the caller and the team.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// <see cref="ControlPlaneAuthority"/>, or <see cref="ControlPlaneAuthority.None"/> when the caller holds no role.
    /// </returns>
    ValueTask<ControlPlaneAuthority> GetAsync(
        ControlPlaneAuthorityRequest request,
        CancellationToken cancellationToken
    );
}
