// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Stops the host as it starts when <c>Directory:Url</c> is set and <c>Cell:ControlPlane:Issuer</c> is not a service
/// URL, so a cell that cannot authenticate to the directory fails at start instead of the first time it reserves
/// an identifier. The composition root makes this check because it reads two options types; it runs as a start check
/// rather than an options validator, because a validator reads no other options.
/// </summary>
/// <param name="cellOptions">The options of the cell, which name the control-plane issuer.</param>
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
