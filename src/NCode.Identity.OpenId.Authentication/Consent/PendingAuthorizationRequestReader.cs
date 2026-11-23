// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Messages;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue.Models;
using NCode.Identity.OpenId.Authentication.Persistence;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Consent;

/// <summary>
/// Reads the authorization request that an interactive surface's return URL names: the request the authorization
/// endpoint parked as a continuation, without consuming it.
/// </summary>
internal class PendingAuthorizationRequestReader(IPersistedGrantService persistedGrantService)
{
    private IPersistedGrantService PersistedGrantService { get; } = persistedGrantService;

    /// <summary>
    /// Reads the parked authorization request.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> instance associated with the current
    /// request.</param>
    /// <param name="returnUrl">The return URL that carries the state of the parked request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// authorization request, or <see langword="null"/> when the URL names no active authorization
    /// continuation.</returns>
    public virtual async ValueTask<IAuthorizationRequest?> ReadAsync(
        OpenIdContext openIdContext,
        string returnUrl,
        CancellationToken cancellationToken
    )
    {
        var state = ContinueUrls.GetStateOrDefault(returnUrl);
        if (string.IsNullOrEmpty(state))
            return null;

        var grantId = PersistedGrantService.CreateGrantId(
            openIdContext.Tenant.TenantId,
            OpenIdConstants.PersistedGrantTypes.Continue,
            state
        );

        var grant = await PersistedGrantService.GetOrDefaultAsync<ContinueEnvelope>(
            openIdContext,
            grantId,
            cancellationToken
        );

        if (grant is not { Status: PersistedGrantStatus.Active } active)
            return null;

        var envelope = active.Payload;
        if (
            !string.Equals(
                envelope.Code,
                OpenIdConstants.ContinueCodes.Authorization,
                StringComparison.Ordinal
            )
        )
            return null;

        return envelope.PayloadJson.Deserialize<IAuthorizationRequest>(
            openIdContext.Environment.JsonSerializerOptions
        );
    }
}
