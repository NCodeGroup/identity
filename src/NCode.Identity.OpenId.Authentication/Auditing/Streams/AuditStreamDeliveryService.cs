// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Delivers the audit records of every tenant's audit streams every
/// <see cref="AuditRecordOptions.StreamPollInterval"/>. Each poll reads the streams whose delivery is due, claims each
/// one's lease in a unit of work of its own so that only one replica delivers a stream, and hands each claimed stream
/// to the <see cref="IAuditStreamDeliverer"/>. A poll that fails is logged and run again at the next interval.
/// </summary>
// See ADR-1534.
internal sealed class AuditStreamDeliveryService(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IAuditStreamDeliverer deliverer,
    IOptions<AuditRecordOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditStreamDeliveryService> logger
) : BackgroundService
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private IAuditStreamDeliverer Deliverer { get; } = deliverer;

    private AuditRecordOptions Options { get; } = options.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ILogger<AuditStreamDeliveryService> Logger { get; } = logger;

    /// <summary>
    /// Gets the identifier with which this replica claims the leases of the streams it delivers.
    /// </summary>
    internal string LeaseHolder { get; } = Guid.CreateVersion7().ToString("N");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
            return;

        using var timer = new PeriodicTimer(Options.StreamPollInterval, TimeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PollAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Reads the delivery states that are due, claims the lease of each one in a unit of work of its own, skipping the
    /// ones that another replica claimed first, and delivers each claimed stream in turn.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    internal async ValueTask PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The poll reads the delivery states of every tenant.
            using var _ = AmbientTenantAccessor.BeginUnscoped("audit stream delivery");

            var due = await ReadDueStatesAsync(cancellationToken);

            var claimed = new List<PersistedAuditStreamState>(due.Count);
            foreach (var state in due)
            {
                if (await TryClaimAsync(state, cancellationToken))
                {
                    claimed.Add(state);
                }
            }

            foreach (var state in claimed)
            {
                await Deliverer.DeliverAsync(state, cancellationToken);
            }
        }
        // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.AuditStreamPollFailed(exception);
        }
    }

    private async ValueTask<IReadOnlyList<PersistedAuditStreamState>> ReadDueStatesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();
        var now = TimeProvider.GetUtcNow();
        return await store.GetDueStatesAsync(now, Options.StreamsPerPoll, cancellationToken);
    }

    private async ValueTask<bool> TryClaimAsync(
        PersistedAuditStreamState state,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        state.LeaseHolder = LeaseHolder;
        state.LeaseExpiryTime = TimeProvider.GetUtcNow() + Options.StreamLeaseDuration;
        await store.UpdateStateAsync(state, cancellationToken);

        try
        {
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        catch (StoreConcurrencyException)
        {
            // Another replica claimed the stream first.
            return false;
        }

        return true;
    }
}
