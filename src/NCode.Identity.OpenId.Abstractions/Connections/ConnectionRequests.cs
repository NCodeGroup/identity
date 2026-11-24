// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Reads the connection a request names.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionRequests
{
    /// <summary>
    /// Gets the connection name a request asks for: the <c>connection</c> parameter, else the text after the
    /// <c>idp:</c> prefix of the first <c>acr_values</c> item that has it.
    /// </summary>
    /// <param name="connectionParameter">The value of the <c>connection</c> request parameter.</param>
    /// <param name="acrValues">The <c>acr_values</c> of the request.</param>
    /// <returns>The requested connection name, or <see langword="null"/> when the request names none.</returns>
    public static string? GetRequestedName(
        string? connectionParameter,
        IReadOnlyCollection<string> acrValues
    )
    {
        if (!string.IsNullOrEmpty(connectionParameter))
        {
            return connectionParameter;
        }

        const string prefix = OpenIdConstants
            .AuthenticationContextClassReferencePrefixes
            .IdentityProvider;
        foreach (var value in acrValues)
        {
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = value[prefix.Length..];
            return name.Length == 0 ? null : name;
        }

        return null;
    }
}
