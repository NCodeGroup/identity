// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Options;

/// <summary>
/// Validates <see cref="DirectoryServerOptions"/>: the directory names the issuer of the control-plane cell, and the
/// issuer is an absolute <c>https</c> URL (or an <c>http</c> URL on a loopback host) of at most
/// <see cref="DirectoryLimits.MaxIssuerLength"/> characters with no user information, query or fragment, because the
/// directory calls the issuer's discovery endpoint to validate a bearer token. The directory also names the
/// control-plane cell, which alone writes the platform and team documents. Every failure is reported at once, and no
/// message repeats a configured issuer, which could carry a credential.
/// </summary>
internal sealed class DirectoryServerOptionsValidator : IValidateOptions<DirectoryServerOptions>
{
    private static string IssuerKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.Issuer)}";

    private static string ControlPlaneCellKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.ControlPlaneCell)}";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryServerOptions options)
    {
        List<string> failures = [];

        var issuer = options.Issuer;
        if (string.IsNullOrWhiteSpace(issuer))
        {
            failures.Add($"{IssuerKey} is required.");
        }
        else if (!IsAllowedIssuer(issuer))
        {
            failures.Add(
                $"{IssuerKey} must be an absolute https URL (or http on a loopback host) of at most {DirectoryLimits.MaxIssuerLength} characters with no user information, query or fragment."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ControlPlaneCell))
            failures.Add($"{ControlPlaneCellKey} is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool IsAllowedIssuer(string issuer)
    {
        if (issuer.Length > DirectoryLimits.MaxIssuerLength || issuer.Any(char.IsWhiteSpace))
            return false;

        if (!AbsoluteUri.TryParse(issuer, out var uri))
            return false;

        if (
            !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
        )
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
