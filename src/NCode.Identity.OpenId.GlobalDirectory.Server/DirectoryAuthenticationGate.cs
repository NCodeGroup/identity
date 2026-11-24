// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// The authentication gate of the directory API. It runs before the route binds its body, validates the bearer access
/// token of a request with <see cref="DirectoryTokenIssuer"/>, which trusts only the tokens that the directory issued
/// itself, for the audience <see cref="GlobalDirectoryProtocol.DirectoryAudience"/>, and requires the scope
/// <see cref="GlobalDirectoryProtocol.DirectoryScope"/>. A request that lacks a valid token with the scope is answered
/// <c>401 Unauthorized</c>, never allowed. A token bound to a key (one that carries a <c>cnf</c> claim) is answered
/// <c>401 Unauthorized</c>, because the directory checks no proof of possession. A request that passes carries a
/// principal whose <c>client_id</c> claim names the calling machine client. A request refused here costs no more than
/// its headers.
/// </summary>
// See ADR-1536.
internal sealed class DirectoryAuthenticationGate(DirectoryTokenIssuer tokenIssuer) : IEndpointGate
{
    /// <summary>
    /// The claim type of the machine client that the request authenticates as.
    /// </summary>
    internal const string ClientIdClaimType = "client_id";

    private const string ScopeClaimType = "scope";
    private const string BearerPrefix = "Bearer ";
    private const string AuthenticationType = "DirectoryBearer";

    /// <summary>
    /// Builds the <c>401 Unauthorized</c> problem document of a request that lacks a valid access token with the
    /// directory scope, and sets the <c>WWW-Authenticate: Bearer</c> response header that tells the caller how to
    /// authenticate.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> whose response receives the header.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult Unauthorized(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        return TypedResults.Problem(
            title: "Unauthorized",
            detail: "A valid access token with the directory scope is required.",
            statusCode: StatusCodes.Status401Unauthorized
        );
    }

    /// <summary>
    /// Reads the bearer access token from the <c>Authorization</c> request header. The <c>Bearer</c> scheme name is
    /// matched without regard to case.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> whose request carries the header.</param>
    /// <param name="token">The trimmed access token when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns>
    /// <c>true</c> when the header uses the <c>Bearer</c> scheme and holds a non-empty token; otherwise <c>false</c>.
    /// </returns>
    private static bool TryGetBearerToken(HttpContext httpContext, out string token)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = authorization[BearerPrefix.Length..].Trim();
            return token.Length > 0;
        }

        token = string.Empty;
        return false;
    }

    /// <summary>
    /// Determines whether the <c>scope</c> claim of an access token grants
    /// <see cref="GlobalDirectoryProtocol.DirectoryScope"/>. The claim is read as a space-delimited string or as an
    /// array of strings, and the scope is compared case-sensitively.
    /// </summary>
    /// <param name="payload">The claims of the validated access token.</param>
    /// <returns>
    /// <c>true</c> when the claim is a string or an array that names the directory scope; otherwise <c>false</c>,
    /// including when the claim is missing or has any other shape.
    /// </returns>
    private static bool HasScope(JsonElement payload)
    {
        if (!payload.TryGetProperty(ScopeClaimType, out var scope))
            return false;

        switch (scope.ValueKind)
        {
            case JsonValueKind.String:
                var text = scope.GetString() ?? string.Empty;
                foreach (var candidate in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (
                        string.Equals(
                            candidate,
                            GlobalDirectoryProtocol.DirectoryScope,
                            StringComparison.Ordinal
                        )
                    )
                        return true;
                }

                return false;

            case JsonValueKind.Array:
                foreach (var element in scope.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                        continue;

                    var candidate = element.GetString();
                    if (
                        string.Equals(
                            candidate,
                            GlobalDirectoryProtocol.DirectoryScope,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Reads the <c>client_id</c> claim, which names the machine client that the access token was issued to.
    /// </summary>
    /// <param name="payload">The claims of the validated access token.</param>
    /// <param name="clientId">The client identifier when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns>
    /// <c>true</c> when the claim is a non-empty string; otherwise <c>false</c>, including when the claim is missing.
    /// </returns>
    private static bool TryGetClientId(JsonElement payload, out string clientId)
    {
        if (
            payload.TryGetProperty(ClientIdClaimType, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } value
        )
        {
            clientId = value;
            return true;
        }

        clientId = string.Empty;
        return false;
    }

    private DirectoryTokenIssuer TokenIssuer { get; } = tokenIssuer;

    /// <inheritdoc />
    public async ValueTask<IResult?> InvokeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetBearerToken(httpContext, out var token))
            return Unauthorized(httpContext);

        var result = await TokenIssuer.ValidateAsync(token, cancellationToken);

        if (!result.IsValid)
            return Unauthorized(httpContext);

        var payload = result.Payload;
        if (
            payload.ValueKind != JsonValueKind.Object
            || !HasScope(payload)
            || !TryGetClientId(payload, out var clientId)
        )
        {
            return Unauthorized(httpContext);
        }

        // The directory accepts unbound bearer tokens only: it checks no proof of possession, so a bound token is
        // refused.
        if (payload.TryGetProperty(JoseClaimNames.Payload.Cnf, out _))
            return Unauthorized(httpContext);

        var clientIdClaim = new Claim(ClientIdClaimType, clientId);
        var scopeClaim = new Claim(ScopeClaimType, GlobalDirectoryProtocol.DirectoryScope);
        var identity = new ClaimsIdentity([clientIdClaim, scopeClaim], AuthenticationType);
        httpContext.User = new ClaimsPrincipal(identity);

        return null;
    }
}
