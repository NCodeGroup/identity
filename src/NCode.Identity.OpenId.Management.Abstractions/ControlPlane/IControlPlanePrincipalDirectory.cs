// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Management.Contracts;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Describes the control-plane principals that a query names by identifier or username. The control plane holds the
/// principals, so a cell reads them from its own store when it is the control plane, and from the control plane through
/// a remote adapter otherwise.
/// </summary>
[PublicAPI]
public interface IControlPlanePrincipalDirectory
{
    /// <summary>
    /// Finds the summaries of the control-plane principals that a query names.
    /// </summary>
    /// <param name="query">The <see cref="ControlPlanePrincipalQuery"/> that names the principals.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the summary of each
    /// principal the query names, each at most once.
    /// </returns>
    ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    );
}
