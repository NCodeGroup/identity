// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Provides the adapter of <see cref="IControlPlanePrincipalDirectory"/> of a workload cell on a platform with a global
/// directory. The cell holds no control-plane principal names, so it describes only the principals of its own
/// management tenant, which is the cell's root tenant, through the local adapter, and describes none when it has no
/// management tenant.
/// </summary>
// See ADR-1536.
internal sealed class CellPrincipalDirectory(
    IPlatformTopology platformTopology,
    IControlPlanePrincipalDirectory localDirectory
) : IControlPlanePrincipalDirectory
{
    private IPlatformTopology PlatformTopology { get; } = platformTopology;
    private IControlPlanePrincipalDirectory LocalDirectory { get; } = localDirectory;

    /// <inheritdoc />
    public bool DescribesEveryPrincipal => false;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        if (!PlatformTopology.HasManagementTenant)
        {
            return ValueTask.FromResult<IReadOnlyList<PrincipalSummaryResource>>([]);
        }

        return LocalDirectory.FindAsync(query, cancellationToken);
    }
}
