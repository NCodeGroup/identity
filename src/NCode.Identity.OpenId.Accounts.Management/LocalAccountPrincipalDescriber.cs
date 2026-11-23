// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Describes the principal of a local account, named by its principal identifier or by its username. A principal is a
/// local account's when it owns the account's self-issued identity.
/// </summary>
// See ADR-1465.
internal sealed class LocalAccountPrincipalDescriber : IPrincipalDescriber
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        IStoreManager storeManager,
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();
        var described = new HashSet<string>(StringComparer.Ordinal);
        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();

        foreach (var principalId in query.PrincipalIds.Distinct(StringComparer.Ordinal))
        {
            var identities = await identityStore.GetByPrincipalAsync(
                principalId,
                cancellationToken
            );
            var identity = identities.FirstOrDefault(x =>
                string.Equals(x.Issuer, AccountConstants.SelfIssuer, StringComparison.Ordinal)
            );
            if (identity is null)
            {
                continue;
            }

            var account = await accountStore.GetByIdOrDefaultAsync(
                identity.Subject,
                cancellationToken
            );
            if (account is null || !described.Add(principalId))
            {
                continue;
            }

            var summary = ToSummary(principalId, account);
            summaries.Add(summary);
        }

        foreach (var userName in query.UserNames.Distinct(StringComparer.Ordinal))
        {
            var account = await accountStore.GetByUserNameOrDefaultAsync(
                userName,
                cancellationToken
            );
            if (account is null)
            {
                continue;
            }

            var identity = await identityStore.GetByIssuerSubjectAsync(
                AccountConstants.SelfIssuer,
                account.LocalAccountId,
                cancellationToken
            );
            if (identity is null || !described.Add(identity.PrincipalId))
            {
                continue;
            }

            var summary = ToSummary(identity.PrincipalId, account);
            summaries.Add(summary);
        }

        return summaries;
    }

    private static PrincipalSummaryResource ToSummary(
        string principalId,
        PersistedLocalAccount account
    ) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = account.DisplayName,
            UserName = account.UserName,
        };
}
