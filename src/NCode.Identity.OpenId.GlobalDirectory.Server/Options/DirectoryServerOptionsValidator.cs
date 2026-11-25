// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Options;

/// <summary>
/// Validates <see cref="DirectoryServerOptions"/>: the directory names its own issuer, its token key ring and the
/// platform's trust anchor, and each issuer is an absolute <c>https</c> URL (or an <c>http</c> URL on a loopback host)
/// of at most <see cref="DirectoryLimits.MaxIssuerLength"/> characters with no user information, query or fragment. The
/// token lifetime lies between <see cref="DirectoryServerOptions.MinTokenLifetime"/> and
/// <see cref="DirectoryServerOptions.MaxTokenLifetime"/>. The anchor names the control-plane cell, which alone writes
/// the platform and team documents, by a cell name of the form <see cref="CellLabelRules"/> describes, with a region
/// and a kind of the same form, and that cell's root tenant by an identifier that
/// <see cref="TenantIdentifierRules.IsValidTenantId"/> accepts. Its client key set must be a public JSON Web Key Set
/// that <see cref="IPublicJwksReader"/> reads. Every failure is reported at once; a refused token lifetime, cell name,
/// region, kind or tenant identifier is named in its message, because none carries a secret, and no message repeats a
/// configured issuer, which could carry a credential.
/// </summary>
internal sealed class DirectoryServerOptionsValidator(IPublicJwksReader jwksReader)
    : IValidateOptions<DirectoryServerOptions>
{
    /// <summary>
    /// Builds the configuration key of a top-level option.
    /// </summary>
    /// <param name="name">The name of the option.</param>
    /// <returns>The key, which starts with the options section.</returns>
    private static string Key(string name) => $"{DirectoryServerOptions.SectionName}:{name}";

    /// <summary>
    /// Builds the configuration key of an option of the control plane.
    /// </summary>
    /// <param name="name">The name of the control-plane option.</param>
    /// <returns>The key, which starts with the control-plane section.</returns>
    private static string ControlPlaneKey(string name) =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.ControlPlane)}:{name}";

    private IPublicJwksReader JwksReader { get; } = jwksReader;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryServerOptions options)
    {
        List<string> failures = [];

        var issuerKey = Key(nameof(DirectoryServerOptions.Issuer));
        ValidateIssuer(failures, issuerKey, options.Issuer);

        if (string.IsNullOrWhiteSpace(options.KeyRing))
        {
            var keyRingKey = Key(nameof(DirectoryServerOptions.KeyRing));
            failures.Add($"{keyRingKey} is required.");
        }

        var lifetime = options.TokenLifetime;
        if (
            lifetime < DirectoryServerOptions.MinTokenLifetime
            || lifetime > DirectoryServerOptions.MaxTokenLifetime
        )
        {
            var lifetimeKey = Key(nameof(DirectoryServerOptions.TokenLifetime));
            failures.Add(
                $"{lifetimeKey} must be between {DirectoryServerOptions.MinTokenLifetime} and {DirectoryServerOptions.MaxTokenLifetime}; it is {lifetime}."
            );
        }

        var controlPlane = options.ControlPlane;

        ValidateLabel(failures, nameof(DirectoryControlPlaneOptions.Cell), controlPlane.Cell);
        ValidateLabel(failures, nameof(DirectoryControlPlaneOptions.Region), controlPlane.Region);
        ValidateLabel(failures, nameof(DirectoryControlPlaneOptions.Kind), controlPlane.Kind);

        var tenantIdKey = ControlPlaneKey(nameof(DirectoryControlPlaneOptions.TenantId));
        var tenantId = controlPlane.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            failures.Add($"{tenantIdKey} is required.");
        }
        else if (!TenantIdentifierRules.IsValidTenantId(tenantId))
        {
            failures.Add(
                $"{tenantIdKey} must be a tenant identifier: 1 to {TenantIdentifierRules.MaxTenantIdLength} lowercase letters, digits or hyphens that start and end with a letter or digit; it is '{tenantId}'."
            );
        }

        var anchorIssuerKey = ControlPlaneKey(nameof(DirectoryControlPlaneOptions.Issuer));
        ValidateIssuer(failures, anchorIssuerKey, controlPlane.Issuer);

        ValidateClientJwks(failures, controlPlane.ClientJwks);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Adds a failure when a control-plane label is blank or too long.
    /// </summary>
    /// <param name="failures">The failures to add to.</param>
    /// <param name="property">The name of the control-plane option.</param>
    /// <param name="value">The value to check.</param>
    private static void ValidateLabel(List<string> failures, string property, string value)
    {
        var key = ControlPlaneKey(property);
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{key} is required.");
        }
        else if (!CellLabelRules.IsValidLabel(value))
        {
            failures.Add(
                $"{key} must be a cell label: {CellLabelRules.Description}; it is '{value}'."
            );
        }
    }

    /// <summary>
    /// Adds a failure when an issuer is blank or not an allowed issuer URL.
    /// </summary>
    /// <param name="failures">The failures to add to.</param>
    /// <param name="key">The configuration key to name in a failure.</param>
    /// <param name="issuer">The issuer to check.</param>
    private static void ValidateIssuer(List<string> failures, string key, string issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            failures.Add($"{key} is required.");
        }
        else if (!IsAllowedIssuer(issuer))
        {
            failures.Add(
                $"{key} must be an absolute https URL (or http on a loopback host) of at most {DirectoryLimits.MaxIssuerLength} characters with no user information, query or fragment."
            );
        }
    }

    /// <summary>
    /// Adds a failure when the control plane's client key set is blank or not a public JSON Web Key Set.
    /// </summary>
    /// <param name="failures">The failures to add to.</param>
    /// <param name="clientJwks">The key set to check.</param>
    private void ValidateClientJwks(List<string> failures, string clientJwks)
    {
        var key = ControlPlaneKey(nameof(DirectoryControlPlaneOptions.ClientJwks));
        if (string.IsNullOrWhiteSpace(clientJwks))
        {
            failures.Add($"{key} is required.");
            return;
        }

        JsonElement jwks;
        try
        {
            jwks = JsonElement.Parse(clientJwks);
        }
        catch (JsonException)
        {
            failures.Add($"{key} must be JSON.");
            return;
        }

        if (!JwksReader.TryRead(jwks, out _, out var error))
            failures.Add($"{key} must be a public JSON Web Key Set: {error}");
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
