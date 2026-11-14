// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Registers the cell with the global directory when the host starts, with the <see cref="CellDescriptor"/> built from
/// <see cref="CellOptions"/>. A directory that cannot be reached throws <see cref="DirectoryUnavailableException"/>,
/// which is logged and retried with an exponential backoff from one second up to sixty seconds, until the
/// registration succeeds or the host stops. Any other exception propagates.
/// </summary>
internal sealed class CellRegistrationService(
    ICellDirectory cellDirectory,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider,
    ILogger<CellRegistrationService> logger
) : BackgroundService
{
    /// <summary>
    /// The longest delay between two attempts to register the cell.
    /// </summary>
    private static TimeSpan MaxDelay { get; } = TimeSpan.FromSeconds(60);

    private ICellDirectory CellDirectory { get; } = cellDirectory;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ILogger<CellRegistrationService> Logger { get; } = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RegisterAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Registers the cell with the directory, retrying <see cref="DirectoryUnavailableException"/> with a capped
    /// exponential backoff.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask RegisterAsync(CancellationToken cancellationToken)
    {
        var descriptor = new CellDescriptor
        {
            Name = CellOptions.Name,
            Region = CellOptions.Region,
            Kind = CellOptions.Kind,
            ManagementUrl = CellOptions.ManagementUrl,
            IsControlPlane = CellOptions.IsControlPlane,
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CellDirectory.RegisterAsync(descriptor, cancellationToken);
                return;
            }
            catch (DirectoryUnavailableException exception)
            {
                var delay = GetDelay(attempt);
                Logger.CellRegistrationFailed(exception, attempt, delay);
                await Task.Delay(delay, TimeProvider, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Gets the delay that follows a failed attempt: one second doubled for each earlier failure, capped at
    /// <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attempt">The one-based number of the attempt that failed.</param>
    /// <returns>The <see cref="TimeSpan"/> to wait before the next attempt.</returns>
    internal static TimeSpan GetDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), MaxDelay.TotalSeconds));
}
