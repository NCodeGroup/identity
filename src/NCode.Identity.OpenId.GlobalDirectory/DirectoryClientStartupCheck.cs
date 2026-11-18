// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Stops the host as it starts when <c>Directory:Url</c> is set and <c>Cell:ControlPlane:Issuer</c> is not a service
/// URL, <c>Directory:ClientId</c> is not <c>cell-</c> followed by <c>Cell:Name</c>, or <c>Cell:ManagementUrl</c> is
/// blank, so a cell that cannot authenticate to the directory or register with it fails at start instead of the first
/// time it reserves an identifier. The composition root makes this check because it reads two options types; it runs as
/// a start check rather than an options validator, because a validator reads no other options.
/// </summary>
/// <param name="cellOptions">
/// The options of the cell, which name the control-plane issuer, the cell and its management URL.
/// </param>
/// <param name="directoryOptions">The options of the directory client.</param>
// See ADR-1285 (the endpoint rule), ADR-1137 (configuration is validated at startup) and project-template ADR-0106.
internal sealed class DirectoryClientStartupCheck(
    IOptions<CellOptions> cellOptions,
    IOptions<DirectoryClientOptions> directoryOptions
) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(directoryOptions.Value.Url))
            return Task.CompletedTask;

        var issuer = cellOptions.Value.ControlPlane.Issuer;
        if (!AbsoluteUri.TryParseServiceUrl(issuer, out _))
        {
            throw new InvalidOperationException(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ControlPlane)}:{nameof(ControlPlaneOptions.Issuer)} '{DirectoryUrls.Describe(issuer)}' must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        var cellClientId = GlobalDirectoryProtocol.CellClientIdPrefix + cellOptions.Value.Name;
        if (!string.Equals(directoryOptions.Value.ClientId, cellClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.ClientId)} must be '{cellClientId}', '{GlobalDirectoryProtocol.CellClientIdPrefix}' followed by {CellOptions.SectionName}:{nameof(CellOptions.Name)}, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        if (string.IsNullOrWhiteSpace(cellOptions.Value.ManagementUrl))
        {
            throw new InvalidOperationException(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ManagementUrl)} must name the URL of the cell's management API, which the cell registers with the directory, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
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
