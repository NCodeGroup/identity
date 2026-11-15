// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Options;

/// <summary>
/// Validates <see cref="DirectoryServerOptions"/>: the directory names the issuer of the control-plane cell, and the
/// issuer is an absolute <c>https</c> URL (or an <c>http</c> URL on a loopback host) with no query or fragment, because
/// the directory calls the issuer's discovery endpoint to validate a bearer token.
/// </summary>
internal sealed class DirectoryServerOptionsValidator : IValidateOptions<DirectoryServerOptions>
{
    private static string IssuerKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.Issuer)}";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryServerOptions options)
    {
        var issuer = options.Issuer;
        if (string.IsNullOrWhiteSpace(issuer))
            return ValidateOptionsResult.Fail($"{IssuerKey} is required.");

        return IsAllowedIssuer(issuer)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{IssuerKey} '{issuer}' must be an absolute https URL, or an http URL on a loopback host, with no query or fragment."
            );
    }

    private static bool IsAllowedIssuer(string issuer)
    {
        if (issuer.Any(char.IsWhiteSpace))
            return false;

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
            return false;

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
