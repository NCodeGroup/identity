// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Provides the <see cref="IControlPlanePrincipalDirectory"/> of a cell that is not the control plane. The cell holds
/// no control-plane principal names, so it describes no principal.
/// </summary>
// See ADR-1536.
internal sealed class EmptyControlPlanePrincipalDirectory : IControlPlanePrincipalDirectory
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<IReadOnlyList<PrincipalSummaryResource>>([]);
}
