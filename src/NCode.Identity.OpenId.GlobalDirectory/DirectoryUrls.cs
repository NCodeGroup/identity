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
    /// The scheme, host, port and path when <paramref name="value"/> is written as an absolute URL, that is, as its
    /// scheme followed by <c>://</c> and an authority; otherwise, the value when it holds no <c>@</c>; otherwise, a
    /// fixed text.
    /// </returns>
    /// <remarks>
    /// The text as written decides, because <see cref="AbsoluteUri.TryParse"/> also reads text such as
    /// <c>user:secret@host</c> as a scheme followed by an opaque remainder, and printing that would print the
    /// credentials.
    /// </remarks>
    public static string Describe(string? value)
    {
        if (
            AbsoluteUri.TryParse(value, out var uri)
            && value
                .AsSpan(uri.Scheme.Length)
                .StartsWith(Uri.SchemeDelimiter, StringComparison.Ordinal)
        )
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
