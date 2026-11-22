// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Options;

/// <summary>
/// Validates <see cref="DirectoryServerOptions"/>: the directory names the issuer of the control-plane cell, and the
/// issuer is an absolute <c>https</c> URL (or an <c>http</c> URL on a loopback host) of at most
/// <see cref="DirectoryLimits.MaxIssuerLength"/> characters with no user information, query or fragment, because the
/// directory calls the issuer's discovery endpoint to validate a bearer token. The directory also names the
/// control-plane cell, which alone writes the platform and team documents, by a cell name of the form
/// <see cref="CellLabelRules"/> describes. It also names that cell's root tenant by an identifier that
/// <see cref="TenantIdentifierRules.IsValidTenantId"/> accepts, which the directory holds for that cell. Every failure
/// is reported at once; a refused cell name or tenant identifier is named in its message, because neither carries a
/// secret, and no message repeats a configured issuer, which could carry a credential.
/// </summary>
internal sealed class DirectoryServerOptionsValidator : IValidateOptions<DirectoryServerOptions>
{
    /// <summary>
    /// Gets the configuration key of <see cref="DirectoryServerOptions.Issuer"/>, which the messages that refuse it
    /// name.
    /// </summary>
    private static string IssuerKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.Issuer)}";

    /// <summary>
    /// Gets the configuration key of <see cref="DirectoryServerOptions.ControlPlaneCell"/>, which the messages that
    /// refuse it name.
    /// </summary>
    private static string ControlPlaneCellKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.ControlPlaneCell)}";

    /// <summary>
    /// Gets the configuration key of <see cref="DirectoryServerOptions.ControlPlaneTenantId"/>, which the messages that
    /// refuse it name.
    /// </summary>
    private static string ControlPlaneTenantIdKey =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.ControlPlaneTenantId)}";

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

        var controlPlaneCell = options.ControlPlaneCell;
        if (string.IsNullOrWhiteSpace(controlPlaneCell))
        {
            failures.Add($"{ControlPlaneCellKey} is required.");
        }
        else if (!CellLabelRules.IsValidLabel(controlPlaneCell))
        {
            failures.Add(
                $"{ControlPlaneCellKey} must be a cell name: {CellLabelRules.Description}; it is '{controlPlaneCell}'."
            );
        }

        var controlPlaneTenantId = options.ControlPlaneTenantId;
        if (string.IsNullOrWhiteSpace(controlPlaneTenantId))
        {
            failures.Add($"{ControlPlaneTenantIdKey} is required.");
        }
        else if (!TenantIdentifierRules.IsValidTenantId(controlPlaneTenantId))
        {
            failures.Add(
                $"{ControlPlaneTenantIdKey} must be a tenant identifier: 1 to {TenantIdentifierRules.MaxTenantIdLength} lowercase letters, digits or hyphens that start and end with a letter or digit; it is '{controlPlaneTenantId}'."
            );
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Determines whether an issuer is one the directory accepts: at most <see cref="DirectoryLimits.MaxIssuerLength"/>
    /// characters, and a service URL that <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts.
    /// </summary>
    /// <param name="issuer">The configured issuer to check.</param>
    /// <returns><c>true</c> when the directory accepts the issuer; otherwise, <c>false</c>.</returns>
    private static bool IsAllowedIssuer(string issuer) =>
        issuer.Length <= DirectoryLimits.MaxIssuerLength
        && AbsoluteUri.TryParseServiceUrl(issuer, out _);
}
