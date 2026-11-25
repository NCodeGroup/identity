// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Reads the access token of a management request from its <c>Authorization</c> header, so every management token
/// validator parses the header by one rule.
/// </summary>
[PublicAPI]
public static class ManagementAuthorizationHeader
{
    /// <summary>
    /// Reads the access token from the <c>Authorization</c> header of a request when the header names a scheme. The
    /// header must hold the scheme name, one space and the token. The scheme name matches without regard to case and
    /// the token is trimmed.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="scheme">The authentication scheme, such as <c>DPoP</c> or <c>Bearer</c>.</param>
    /// <param name="token">When this method returns <see langword="true"/>, the non-empty access token.</param>
    /// <returns>
    /// <see langword="true"/> when the header names <paramref name="scheme"/> and carries a non-empty token; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryReadToken(
        HttpContext httpContext,
        string scheme,
        [NotNullWhen(true)] out string? token
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(scheme);

        var authorization = httpContext.Request.Headers.Authorization.ToString();
        var prefixLength = scheme.Length + 1;
        if (
            authorization.Length > prefixLength
            && authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            && authorization[scheme.Length] == ' '
        )
        {
            var value = authorization[prefixLength..].Trim();
            if (value.Length > 0)
            {
                token = value;
                return true;
            }
        }

        token = null;
        return false;
    }
}
