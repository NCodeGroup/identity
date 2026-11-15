// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.Server.Cells;

/// <summary>
/// Validates <see cref="CellOptions"/>: a cell names itself and its region with lowercase labels, names its kind, points
/// a cell that is not the control plane at the control plane's issuer, and reaches its management API over <c>https</c>
/// (or <c>http</c> in local development).
/// </summary>
/// <param name="localDevelopmentMode">The mode that decides whether a management URL may use <c>http</c>.</param>
internal sealed class CellOptionsValidator(LocalDevelopmentMode localDevelopmentMode)
    : IValidateOptions<CellOptions>
{
    private static string Prefix => CellOptions.SectionName;

    private LocalDevelopmentMode LocalDevelopmentMode { get; } = localDevelopmentMode;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CellOptions options)
    {
        var failures = new List<string>();

        ValidateLabel(options.Name, $"{Prefix}:{nameof(options.Name)}", failures);
        ValidateLabel(options.Region, $"{Prefix}:{nameof(options.Region)}", failures);

        if (string.IsNullOrWhiteSpace(options.Kind))
        {
            failures.Add($"{Prefix}:{nameof(options.Kind)} is required.");
        }

        if (!options.IsControlPlane)
        {
            var issuerKey =
                $"{Prefix}:{nameof(options.ControlPlane)}:{nameof(options.ControlPlane.Issuer)}";
            var issuer = options.ControlPlane.Issuer;
            if (string.IsNullOrWhiteSpace(issuer))
            {
                failures.Add(
                    $"{issuerKey} is required when {Prefix}:{nameof(options.IsControlPlane)} is false: a cell that "
                        + "is not the control plane accepts platform and team management tokens from its issuer."
                );
            }
            else if (!IsAllowedIssuer(issuer))
            {
                failures.Add(
                    $"{issuerKey} '{issuer}' must be an absolute https URL, or an http URL on a loopback host, with no query or fragment."
                );
            }
        }

        ValidateManagementUrl(options.ManagementUrl, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
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

    private static void ValidateLabel(string? value, string key, List<string> failures)
    {
        if (!CellLabelRules.IsValidLabel(value))
        {
            failures.Add(
                $"{key} '{value}' must start with a lowercase letter, end with a lowercase letter or digit, and "
                    + "hold only lowercase letters, digits and hyphens, at most 31 characters."
            );
        }
    }

    private void ValidateManagementUrl(string? managementUrl, List<string> failures)
    {
        if (string.IsNullOrEmpty(managementUrl))
            return;

        var key = $"{Prefix}:{nameof(CellOptions.ManagementUrl)}";
        if (!Uri.TryCreate(managementUrl, UriKind.Absolute, out var uri))
        {
            failures.Add($"{key} '{managementUrl}' must be an absolute URL.");
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return;

        if (uri.Scheme == Uri.UriSchemeHttp && LocalDevelopmentMode.Enabled)
            return;

        failures.Add(
            LocalDevelopmentMode.Enabled
                ? $"{key} '{managementUrl}' must be an absolute http or https URL."
                : $"{key} '{managementUrl}' must be an absolute https URL."
        );
    }
}
