// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Principals;

/// <summary>
/// Describes the control-plane principals of one kind. Every registered describer is asked, so the describers are a
/// strategy collection.
/// </summary>
// See ADR-1001.
[PublicAPI]
public interface IPrincipalDescriber
{
    /// <summary>
    /// Describes the principals of a query that the describer knows, reading through <paramref name="storeManager"/> in
    /// the control-plane tenant's scope.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> scoped to the control-plane tenant.</param>
    /// <param name="query">The <see cref="ControlPlanePrincipalQuery"/> that names the principals.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the summary of each
    /// principal the describer knows.
    /// </returns>
    ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        IStoreManager storeManager,
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    );
}
