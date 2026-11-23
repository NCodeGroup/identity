// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Redeems the one-time code that an end user presents for a local account.
/// </summary>
// See ADR-1533.
internal static class EmailCodeRedemption
{
    /// <summary>
    /// Checks a code against a flow, confirms that the flow belongs to the account, and spends the flow. The code is
    /// spent only once it is bound to the account, with one atomic take.
    /// </summary>
    /// <param name="emailCodes">The <see cref="ILocalAccountEmailCodes"/> that holds the flow.</param>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the tenant the flow belongs to.</param>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="flowId">The handle of the flow that the code was mailed under.</param>
    /// <param name="code">The code the end user presented, which the caller owns and disposes.</param>
    /// <param name="account">The local account that the code must be bound to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// <see langword="true"/> when the code is right, is bound to the account, and this call took the flow; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static async ValueTask<bool> TryRedeemAsync(
        ILocalAccountEmailCodes emailCodes,
        OpenIdContext openIdContext,
        string purpose,
        string flowId,
        Secret<char> code,
        PersistedLocalAccount account,
        CancellationToken cancellationToken
    )
    {
        var match = await emailCodes.VerifyAsync(
            openIdContext,
            purpose,
            flowId,
            code,
            cancellationToken
        );
        if (match is null || !match.IsFor(account))
        {
            return false;
        }

        return await emailCodes.ConsumeAsync(openIdContext, purpose, flowId, cancellationToken);
    }
}
