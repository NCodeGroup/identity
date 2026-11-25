// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Management.Contracts;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Describes the control-plane principals that a query names by identifier or username. The control-plane cell holds
/// the principals and describes them from its own store. A workload cell holds no control-plane principal names and
/// describes only the principals of its own management tenant.
/// </summary>
[PublicAPI]
public interface IControlPlanePrincipalDirectory
{
    /// <summary>
    /// Gets a value that indicates whether the directory describes every principal of the control plane. A directory
    /// that does not describe every principal cannot tell an unknown identifier from one it does not hold, so a caller
    /// grants such an identifier without an existence check.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when an identifier the directory cannot describe names no principal; otherwise,
    /// <see langword="false"/>.
    /// </value>
    bool DescribesEveryPrincipal { get; }

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
