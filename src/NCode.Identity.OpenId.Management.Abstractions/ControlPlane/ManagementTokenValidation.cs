// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The outcome of validating a request's management access token: authenticated as a principal, not authenticated, or
/// not validated because the authority that validates it is unavailable.
/// </summary>
[PublicAPI]
public sealed class ManagementTokenValidation
{
    /// <summary>
    /// Gets the outcome of a request whose token is missing or does not authenticate.
    /// </summary>
    public static ManagementTokenValidation Unauthenticated { get; } = new(null, false, default);

    private ManagementTokenValidation(
        ClaimsPrincipal? principal,
        bool isUnavailable,
        TimeSpan retryAfter
    )
    {
        Principal = principal;
        IsUnavailable = isUnavailable;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Creates the outcome of a token that authenticates as the specified principal.
    /// </summary>
    /// <param name="principal">The principal that the token authenticates.</param>
    /// <returns>The <see cref="ManagementTokenValidation"/>.</returns>
    public static ManagementTokenValidation Authenticated(ClaimsPrincipal principal) =>
        new(principal, false, default);

    /// <summary>
    /// Creates the outcome of a token that could not be validated because the authority that validates it is unavailable.
    /// </summary>
    /// <param name="retryAfter">The time after which the caller may retry.</param>
    /// <returns>The <see cref="ManagementTokenValidation"/>.</returns>
    public static ManagementTokenValidation Unavailable(TimeSpan retryAfter) =>
        new(null, true, retryAfter);

    /// <summary>
    /// Gets a value that indicates whether the token authenticates.
    /// </summary>
    public bool IsAuthenticated => Principal is not null;

    /// <summary>
    /// Gets a value that indicates whether the token could not be validated because the authority that validates it is
    /// unavailable.
    /// </summary>
    public bool IsUnavailable { get; }

    /// <summary>
    /// Gets the principal that the token authenticates, or <see langword="null"/> when it does not authenticate.
    /// </summary>
    public ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Gets the time after which the caller may retry when <see cref="IsUnavailable"/> is <see langword="true"/>.
    /// </summary>
    public TimeSpan RetryAfter { get; }
}
