// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// An internal request feature that records whether the authenticated management caller is a client acting for itself,
/// as <see cref="ManagementCallerGate"/> resolves it before the handler changes anything. The audit recorder reads it
/// to name the actor of a change, so the answer does not depend on a principal the change removes.
/// </summary>
// See ADR-1466.
internal sealed class ManagementActorFeature(bool isActingClient)
{
    /// <summary>
    /// Gets a value indicating whether the subject of the caller is the requesting client acting for itself.
    /// </summary>
    public bool IsActingClient { get; } = isActingClient;
}
