// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Provides the <see cref="IControlPlaneAuthority"/> of a cell that is not the control plane. It answers from the
/// platform and team authority documents that the cell's replica holds, and never calls the directory or another cell.
/// A subject that holds no replicated role and is no replicated client has no authority.
/// </summary>
// See ADR-1536.
internal sealed class ReplicatedControlPlaneAuthority(IControlPlaneReplica replica)
    : IControlPlaneAuthority
{
    private IControlPlaneReplica Replica { get; } = replica;

    /// <inheritdoc />
    public async ValueTask<ControlPlaneAuthority> GetAsync(
        ControlPlaneAuthorityRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var authority = await Replica.GetAuthorityAsync(
            request.Subject,
            request.TeamId,
            cancellationToken
        );

        var isActingClient =
            authority.ClientId is { } clientId
            && string.Equals(clientId, request.ClientId, StringComparison.Ordinal);

        if (
            authority.PlatformRoles.Count == 0
            && authority.TeamRoles.Count == 0
            && authority.ClientId is null
        )
        {
            return ControlPlaneAuthority.None;
        }

        return new ControlPlaneAuthority
        {
            PrincipalId = request.Subject,
            IsActingClient = isActingClient,
            PlatformRoles = authority.PlatformRoles,
            TeamRoles = authority.TeamRoles,
            IsGlobalAdmin = authority.PlatformRoles.Any(roleName =>
                string.Equals(
                    roleName,
                    BuiltInRoles.GlobalAdmin,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
        };
    }
}
