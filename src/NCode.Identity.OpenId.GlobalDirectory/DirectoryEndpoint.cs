// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Decides whether a URL may name the directory service or the control-plane issuer: an absolute <c>https</c> URL, or
/// an <c>http</c> URL on a loopback host, with no whitespace, query or fragment.
/// </summary>
// See ADR-1285 (the endpoint rule).
internal static class DirectoryEndpoint
{
    /// <summary>
    /// Gets a value that indicates whether <paramref name="value"/> is an allowed endpoint.
    /// </summary>
    /// <param name="value">The URL to test.</param>
    /// <returns><c>true</c> when the URL is allowed; otherwise, <c>false</c>.</returns>
    public static bool IsAllowed(string? value)
    {
        if (!AbsoluteUri.TryParse(value, out var uri))
            return false;

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
