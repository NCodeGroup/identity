// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// Creates the problem answers that every management route shares.
/// </summary>
[PublicAPI]
public static class ManagementProblems
{
    private const string BearerScheme = "Bearer";
    private const string DPoPScheme = "DPoP";
    private const string InvalidTokenError = "invalid_token";

    private static string ToChallenge(string challengeScheme, string requestScheme)
    {
        var isRequestScheme = string.Equals(
            challengeScheme,
            requestScheme,
            StringComparison.OrdinalIgnoreCase
        );
        return isRequestScheme
            ? $"{challengeScheme} error=\"{InvalidTokenError}\""
            : challengeScheme;
    }

    /// <summary>
    /// Creates the <c>401 Unauthorized</c> problem for a request that carries no valid management access token, and
    /// sets one <c>WWW-Authenticate</c> challenge per scheme the management API accepts, <c>Bearer</c> (RFC 6750 §3)
    /// then <c>DPoP</c> (RFC 9449 §7.1). The challenge of the scheme that the request's <c>Authorization</c> header
    /// used carries <c>error="invalid_token"</c>; a request that carries no credentials gets challenges without an
    /// error code.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> whose response receives the challenges.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    public static ProblemHttpResult AuthenticationRequired(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authorization = httpContext.Request.Headers.Authorization.ToString();
        var separator = authorization.IndexOf(' ');
        var requestScheme = separator < 0 ? authorization : authorization[..separator];
        var bearerChallenge = ToChallenge(BearerScheme, requestScheme);
        var dPoPChallenge = ToChallenge(DPoPScheme, requestScheme);
        string[] challenges = [bearerChallenge, dPoPChallenge];
        httpContext.Response.Headers.WWWAuthenticate = challenges;

        return TypedResults.Problem(
            detail: "Authentication is required.",
            statusCode: StatusCodes.Status401Unauthorized
        );
    }

    /// <summary>
    /// Creates the <c>404 Not Found</c> problem for a resource that the request names and that does not exist.
    /// </summary>
    public static ProblemHttpResult ResourceNotFound() =>
        TypedResults.Problem(
            detail: "The resource does not exist.",
            statusCode: StatusCodes.Status404NotFound
        );
}
