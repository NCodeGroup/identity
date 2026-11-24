// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Sets the key custody of an incoming tenant to held once the global directory places the tenant in this cell. The
/// service does nothing on a platform without a directory. Every <see cref="Interval"/> one replica runs a pass, under
/// a lease on the shared-state port keyed by the window: it lists the tenants whose custody is
/// <see cref="TenantKeyCustody.Incoming"/>, reads each one's placement, and sets the custody of each tenant that the
/// directory places in this cell to <see cref="TenantKeyCustody.Held"/>. A directory that cannot be reached skips the
/// pass, and a tenant that cannot be handled is logged and left for the next pass.
/// </summary>
// See ADR-1536.
internal sealed class TenantKeyCustodyReconciler(
    IPlatformTopology platformTopology,
    ITenantDirectory tenantDirectory,
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IExpiringKeySet expiringKeySet,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider,
    ILogger<TenantKeyCustodyReconciler> logger
) : BackgroundService
{
    /// <summary>
    /// The purpose that prefixes the key of the lease that one replica takes for each window.
    /// </summary>
    internal const string LeasePurpose = "tenant-key-custody-reconciler";

    /// <summary>
    /// Gets the time between two passes.
    /// </summary>
    internal static TimeSpan Interval { get; } = TimeSpan.FromMinutes(15);

    private IPlatformTopology PlatformTopology { get; } = platformTopology;
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IExpiringKeySet ExpiringKeySet { get; } = expiringKeySet;
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptions;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<TenantKeyCustodyReconciler> Logger { get; } = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!PlatformTopology.HasDirectory)
            return;

        try
        {
            await ReconcileAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval, TimeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReconcileAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Runs one pass, when this replica wins the lease of the current window. A pass that fails is logged and run again
    /// at the next interval.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    internal async ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileCoreAsync(cancellationToken);
        }
        catch (DirectoryUnavailableException exception)
        {
            Logger.TenantKeyCustodyDirectoryUnavailable(exception);
        }
        // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.TenantKeyCustodyPassFailed(exception);
        }
    }

    private async ValueTask ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        if (!PlatformTopology.HasDirectory)
            return;

        // Replicas that run in one window share the key, so one of them runs the pass, and the lease expires at the
        // window's end, so the next window's pass takes a lease of its own.
        var window = TimeProvider.GetUtcNow().UtcTicks / Interval.Ticks;
        var windowKey = $"{LeasePurpose}:{window}";
        var leaseExpiresWhen = new DateTimeOffset((window + 1) * Interval.Ticks, TimeSpan.Zero);
        if (!await ExpiringKeySet.TryAddAsync(windowKey, leaseExpiresWhen, cancellationToken))
            return;

        // The pass reads the tenants of every team, outside any request.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(
            "tenant key custody reconciliation"
        );

        IReadOnlyList<string> tenantIds;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var tenantStore = storeManager.GetStore<ITenantStore>();
            tenantIds = await tenantStore.ListTenantIdsByKeyCustodyAsync(
                TenantKeyCustody.Incoming,
                cancellationToken
            );
        }

        var cellName = CellOptionsAccessor.Value.Name;
        foreach (var tenantId in tenantIds)
        {
            var placement = await TenantDirectory.GetPlacementOrDefaultAsync(
                tenantId,
                cancellationToken
            );
            if (placement is null || placement.Cell != cellName)
                continue;

            await HoldAsync(tenantId, cancellationToken);
        }
    }

    private async ValueTask HoldAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenantStore = storeManager.GetStore<ITenantStore>();
        var exists = await tenantStore.SetKeyCustodyAsync(
            tenantId,
            TenantKeyCustody.Held,
            null,
            cancellationToken
        );
        if (!exists)
            return;

        await storeManager.SaveChangesAsync(cancellationToken);
        Logger.TenantKeyCustodyHeld(tenantId);
    }
}
