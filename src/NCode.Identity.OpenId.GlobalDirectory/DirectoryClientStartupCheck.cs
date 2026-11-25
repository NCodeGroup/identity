// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Stops the host as it starts when <c>Directory:Url</c> is set and <c>Directory:ClientId</c> is not <c>cell-</c>
/// followed by <c>Cell:Name</c>, <c>Directory:KeyRing</c> names no key ring of the client library, or
/// <c>Cell:ManagementUrl</c> is blank, and, for a cell that is not the control plane, when
/// <c>Cell:ControlPlane:Issuer</c> is not a service URL or the tenant-resolution strategy is not dynamic
/// (<c>DynamicByHost</c> or <c>DynamicByPath</c>), because every workload cell of a platform with a directory has a
/// management tenant of its own, so a cell that cannot authenticate to the directory or register with it fails at start
/// instead of the first time it reserves an identifier. The composition root makes this check because it reads two
/// options types; it runs as a start check rather than an options validator, because a validator reads no other
/// options.
/// </summary>
/// <param name="cellOptions">
/// The options of the cell, which name the control-plane issuer, the cell and its management URL.
/// </param>
/// <param name="directoryOptions">The options of the directory client.</param>
/// <param name="tenantResolutionOptions">The options of tenant resolution, which name the cell's strategy.</param>
/// <param name="keyRingProvider">The key rings of the client library, which hold the keys that sign the
/// assertion.</param>
// See ADR-1285 (the endpoint rule), ADR-1137 (configuration is validated at startup), ADR-1536 (decision 10, a workload
// cell serves a dynamic strategy) and project-template ADR-0106.
internal sealed class DirectoryClientStartupCheck(
    IOptions<CellOptions> cellOptions,
    IOptions<DirectoryClientOptions> directoryOptions,
    IOptions<TenantResolutionOptions> tenantResolutionOptions,
    IClientKeyRingProvider keyRingProvider
) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(directoryOptions.Value.Url))
            return Task.CompletedTask;

        List<string> failures = [];

        var cellClientId = GlobalDirectoryProtocol.CellClientIdPrefix + cellOptions.Value.Name;
        if (!string.Equals(directoryOptions.Value.ClientId, cellClientId, StringComparison.Ordinal))
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.ClientId)} must be '{cellClientId}', '{GlobalDirectoryProtocol.CellClientIdPrefix}' followed by {CellOptions.SectionName}:{nameof(CellOptions.Name)}, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        var keyRing = directoryOptions.Value.KeyRing;
        if (string.IsNullOrWhiteSpace(keyRing) || !keyRingProvider.HasKeyRing(keyRing))
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.KeyRing)} '{keyRing}' must name a key ring of the {OpenIdClientDefaults.KeyRingsSectionName} section, which holds the keys that sign the cell's client assertion, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        if (string.IsNullOrWhiteSpace(cellOptions.Value.ManagementUrl))
        {
            failures.Add(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ManagementUrl)} must name the URL of the cell's management API, which the cell registers with the directory, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        var issuer = cellOptions.Value.ControlPlane.Issuer;
        if (!cellOptions.Value.IsControlPlane && !AbsoluteUri.TryParseServiceUrl(issuer, out _))
        {
            var describedIssuer = DirectoryUrls.Describe(issuer);
            failures.Add(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ControlPlane)}:{nameof(ControlPlaneOptions.Issuer)} '{describedIssuer}' must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set and the cell is not the control plane."
            );
        }

        var strategyCode = tenantResolutionOptions.Value.StrategyCode;
        var isDynamic =
            strategyCode
            is OpenIdConstants.TenantStrategyCodes.DynamicByHost
                or OpenIdConstants.TenantStrategyCodes.DynamicByPath;
        if (!cellOptions.Value.IsControlPlane && !isDynamic)
        {
            failures.Add(
                $"OpenId:TenantResolution:StrategyCode '{strategyCode}' must be '{OpenIdConstants.TenantStrategyCodes.DynamicByHost}' or '{OpenIdConstants.TenantStrategyCodes.DynamicByPath}' when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set and the cell is not the control plane: every workload cell of a platform with a directory has a management tenant of its own and is managed from its own cell console."
            );
        }

        if (failures.Count > 0)
        {
            var lines = new List<string>
            {
                $"The directory client configuration has {failures.Count} problem(s):",
            };
            var failureLines = failures.Select(failure => $"  - {failure}");
            lines.AddRange(failureLines);
            var message = string.Join(Environment.NewLine, lines);
            throw new InvalidOperationException(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
