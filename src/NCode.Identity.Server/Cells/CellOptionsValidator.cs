// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.Server.Cells;

/// <summary>
/// Validates <see cref="CellOptions"/>: a cell names itself and its region with lowercase labels, names its kind,
/// points a cell that is not the control plane at the control plane's issuer, and reaches its management API over
/// <c>https</c>, or over <c>http</c> in local development. A refused label's failure names the key, the value and the
/// rule; a refused URL's failure names the key and the rule, never the URL, because it may carry a password.
/// </summary>
/// <param name="localDevelopmentMode">
/// The mode that decides whether a management URL may use <c>http</c>.
/// </param>
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
            else if (!AbsoluteUri.TryParseServiceUrl(issuer, out _))
            {
                failures.Add(
                    $"{issuerKey} must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment."
                );
            }
        }

        ValidateManagementUrl(options.ManagementUrl, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateLabel(string? value, string key, List<string> failures)
    {
        if (!CellLabelRules.IsValidLabel(value))
        {
            failures.Add($"{key} '{value}' must be {CellLabelRules.Description}.");
        }
    }

    private void ValidateManagementUrl(string? managementUrl, List<string> failures)
    {
        if (string.IsNullOrEmpty(managementUrl))
            return;

        var isManagementUrl = LocalDevelopmentMode.Enabled
            ? AbsoluteUri.TryParseAnyHttpServiceUrl(managementUrl, out _)
            : AbsoluteUri.TryParseHttpsServiceUrl(managementUrl, out _);
        if (isManagementUrl)
            return;

        var key = $"{Prefix}:{nameof(CellOptions.ManagementUrl)}";
        failures.Add(
            LocalDevelopmentMode.Enabled
                ? $"{key} must be an absolute http or https URL with no user information, query or fragment."
                : $"{key} must be an absolute https URL with no user information, query or fragment."
        );
    }
}
