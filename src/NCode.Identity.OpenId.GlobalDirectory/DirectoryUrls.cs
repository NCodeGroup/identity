// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes a configured URL in a message without its user information, query or fragment, so a credential in a
/// URL never reaches a log.
/// </summary>
internal static class DirectoryUrls
{
    private const string NotAnAbsoluteUrl = "<not an absolute URL>";

    /// <summary>
    /// Gets the text that stands for <paramref name="value"/> in a message.
    /// </summary>
    /// <param name="value">The configured URL or issuer.</param>
    /// <returns>
    /// The scheme, host, port and path when <paramref name="value"/> is an absolute URL; otherwise, the value when it
    /// holds no <c>@</c>; otherwise, a fixed text.
    /// </returns>
    public static string Describe(string? value)
    {
        if (AbsoluteUri.TryParse(value, out var uri))
        {
            var schemeAndServer = uri.GetComponents(
                UriComponents.SchemeAndServer,
                UriFormat.UriEscaped
            );
            return $"{schemeAndServer}{uri.AbsolutePath}";
        }

        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Contains('@', StringComparison.Ordinal) ? NotAnAbsoluteUrl : value;
    }
}
