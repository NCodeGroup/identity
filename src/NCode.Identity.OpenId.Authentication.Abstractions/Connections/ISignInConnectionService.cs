// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Connections;

/// <summary>
/// Offers the connections an interactive sign-in surface lets an end user sign in through, from the authorization
/// request that is parked behind a return URL.
/// </summary>
// See ADR-1535.
[PublicAPI]
public interface ISignInConnectionService
{
    /// <summary>
    /// Gets the connections the sign-in offers: the client's allowed connections whose strategy verifies a password,
    /// narrowed to the requested connection when the parked request names one.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> instance associated with the current request.</param>
    /// <param name="returnUrl">The return URL that names the parked authorization request, if any.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the offered
    /// and the preselected connections.</returns>
    ValueTask<SignInConnections> GetOfferedAsync(
        OpenIdContext openIdContext,
        string? returnUrl,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Selects the connection a sign-in submission signs in through. A posted name must be offered; without one, the
    /// preselected connection is selected.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> instance associated with the current request.</param>
    /// <param name="returnUrl">The return URL that names the parked authorization request, if any.</param>
    /// <param name="postedName">The connection name the submission names, if any.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// selection, which is refused when no offered connection matches.</returns>
    ValueTask<ConnectionSelection> SelectAsync(
        OpenIdContext openIdContext,
        string? returnUrl,
        string? postedName,
        CancellationToken cancellationToken
    );
}
