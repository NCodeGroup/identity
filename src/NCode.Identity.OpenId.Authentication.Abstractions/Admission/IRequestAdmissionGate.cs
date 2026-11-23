// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Admits or sheds a request to this replica's OpenID protocol endpoints by the share of the replica's request capacity
/// its tenant may fill. A request the gate admits holds a place until <see cref="Exit"/> releases it.
/// </summary>
// See ADR-1523.
[PublicAPI]
public interface IRequestAdmissionGate
{
    /// <summary>
    /// Admits the request when the replica's in-flight requests, including this one, stay within the share of the
    /// replica's capacity that the request's tenant may fill, and sheds it otherwise.
    /// </summary>
    /// <param name="capacityPercent">The share, from <c>1</c> to <c>100</c> percent, of the replica's capacity that the
    /// request's tenant may fill.</param>
    /// <returns><see langword="true"/> when the request is admitted, after which the caller calls <see cref="Exit"/>
    /// once the request completes; <see langword="false"/> when it is shed, which holds no place.</returns>
    bool TryEnter(int capacityPercent);

    /// <summary>
    /// Releases the place that a request <see cref="TryEnter"/> admitted holds.
    /// </summary>
    void Exit();
}
