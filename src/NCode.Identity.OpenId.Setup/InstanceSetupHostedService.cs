// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Setup.Logging;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Prepares an uninitialized instance when the host starts: it ensures that a setup token is available through
/// <see cref="ISetupTokenService.EnsureTokenAsync"/>. An initialized instance needs none. Setup belongs to the
/// control-plane cell, so a cell that is not the control plane prepares nothing and logs that once.
/// </summary>
internal sealed class InstanceSetupHostedService(
    IInstanceSetupService instanceSetupService,
    ISetupTokenService setupTokenService,
    IOptions<CellOptions> cellOptions,
    ILogger<InstanceSetupHostedService> logger
) : IHostedService
{
    private IInstanceSetupService InstanceSetupService { get; } = instanceSetupService;

    private ISetupTokenService SetupTokenService { get; } = setupTokenService;

    private IOptions<CellOptions> CellOptions { get; } = cellOptions;

    private ILogger<InstanceSetupHostedService> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!CellOptions.Value.IsControlPlane)
        {
            Logger.SetupBelongsToTheControlPlane();
            return;
        }

        if (await InstanceSetupService.IsInitializedAsync(cancellationToken))
            return;

        await SetupTokenService.EnsureTokenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
