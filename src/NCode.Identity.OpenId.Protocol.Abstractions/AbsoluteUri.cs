// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId;

/// <summary>
/// Parses the absolute URIs that OAuth 2.0 and OpenID Connect messages, metadata and settings carry (RFC 3986 §4.3): text
/// that names its scheme and contains no whitespace. <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> with
/// <see cref="UriKind.Absolute"/> also accepts an implicit file path as a <c>file</c> URI — <c>/path</c> on Unix,
/// <c>C:\path</c> on Windows — and trims surrounding whitespace, so the same text would mean different things on
/// different platforms; this parser accepts neither. A service URL is written with its authority and judged on the
/// text as written, because <see cref="Uri"/> reads an empty <c>?</c>, <c>#</c> or <c>@</c> as absent.
/// </summary>
[PublicAPI]
public static class AbsoluteUri
{
    /// <summary>
    /// Parses the specified text as an absolute URI that names its scheme and contains no whitespace.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns><see langword="true"/> when the text is such an absolute URI; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Uri? uri)
    {
        if (
            !string.IsNullOrEmpty(value)
            && !value.Any(char.IsWhiteSpace)
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && value.StartsWith($"{parsed.Scheme}:", StringComparison.OrdinalIgnoreCase)
        )
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Parses the specified text as an absolute URI, as <see cref="TryParse"/> does, that also names the <c>https</c> scheme.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is such an absolute <c>https</c> URI; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryParseHttps(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Uri? uri
    )
    {
        if (TryParse(value, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Parses the specified text, as <see cref="TryParse"/> does, as the URL of a service: an absolute <c>https</c>
    /// URL, or an <c>http</c> URL on a loopback host, written with its authority and with no user information, query or
    /// fragment.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is such a service URL; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryParseServiceUrl(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Uri? uri
    )
    {
        if (
            TryParseServiceUrlShape(value, out var parsed)
            && (
                parsed.Scheme == Uri.UriSchemeHttps
                || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)
            )
        )
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Parses the specified text as the URL of a service, as <see cref="TryParseServiceUrl"/> does, that names the
    /// <c>https</c> scheme.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is such an <c>https</c> service URL; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryParseHttpsServiceUrl(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Uri? uri
    )
    {
        if (TryParseServiceUrlShape(value, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Parses the specified text as the URL of a service, as <see cref="TryParseServiceUrl"/> does, that names the
    /// <c>https</c> scheme or the <c>http</c> scheme on any host.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is such an <c>https</c> or <c>http</c> service URL; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseAnyHttpServiceUrl(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Uri? uri
    )
    {
        if (
            TryParseServiceUrlShape(value, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
        )
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Parses the specified text, as <see cref="TryParse"/> does, and checks that it is written as a service URL.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="uri">When this method returns <see langword="true"/>, the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is an absolute URI written with its authority and with no user
    /// information, query or fragment; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryParseServiceUrlShape(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Uri? uri
    )
    {
        if (TryParse(value, out var parsed) && HasServiceUrlShape(value, parsed.Scheme))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Determines whether the text, as written, names the scheme followed by <c>://</c> and an authority, holds no
    /// <c>@</c> in that authority, and contains no <c>?</c> or <c>#</c>.
    /// </summary>
    /// <param name="value">The text to check, which <see cref="TryParse"/> parsed.</param>
    /// <param name="scheme">The scheme of the parsed URI.</param>
    /// <returns>
    /// <see langword="true"/> when the text is written that way; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool HasServiceUrlShape(string value, string scheme)
    {
        var text = value.AsSpan();
        if (
            text.ContainsAny('?', '#')
            || !text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        var afterScheme = text[scheme.Length..];
        if (!afterScheme.StartsWith(Uri.SchemeDelimiter, StringComparison.Ordinal))
            return false;

        var authorityAndPath = afterScheme[Uri.SchemeDelimiter.Length..];
        var authorityLength = authorityAndPath.IndexOf('/');
        var authority =
            authorityLength < 0 ? authorityAndPath : authorityAndPath[..authorityLength];
        return !authority.Contains('@');
    }
}
