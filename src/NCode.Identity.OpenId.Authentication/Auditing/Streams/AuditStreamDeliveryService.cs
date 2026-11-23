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
/// <see cref="AuditRecordOptions.StreamPollInterval"/>. Each poll reads the streams whose delivery is due, and for each
/// one in turn takes its lease in a unit of work of its own, so that only one replica delivers a stream, just before
/// handing the stream to the <see cref="IAuditStreamDeliverer"/>. A stream whose claim or delivery fails is logged and
/// the poll goes on with the next one. A poll that fails is logged and run again at the next interval.
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
    /// Reads the delivery states that are due and, for each one in turn, claims its lease in a unit of work of its own
    /// just before delivering it, skipping the ones that another replica took first.
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

            // Each lease is taken just before its delivery, so a stream late in the list has its whole lease.
            foreach (var state in due)
            {
                try
                {
                    if (await TryClaimAsync(state, cancellationToken))
                    {
                        await Deliverer.DeliverAsync(state, cancellationToken);
                    }
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException
                        || !cancellationToken.IsCancellationRequested
                    )
                {
                    // One stream's failure never stops the streams after it.
                    Logger.AuditStreamDeliveryErrored(state.StreamId, state.TenantId, exception);
                }
            }
        }
        // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown, and
        // a failure while stopping is still logged.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            Logger.AuditStreamPollFailed(exception);
        }
    }

    /// <summary>
    /// Reads the delivery states that are due: those whose next attempt has come and whose lease is free or expired.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>At most the configured number of streams per poll, as their delivery states.</returns>
    private async ValueTask<IReadOnlyList<PersistedAuditStreamState>> ReadDueStatesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();
        var now = TimeProvider.GetUtcNow();
        return await store.GetDueStatesAsync(
            now,
            now - Options.StreamSettleDelay,
            Options.StreamsPerPoll,
            cancellationToken
        );
    }

    /// <summary>
    /// Takes the lease of a stream for this replica in a unit of work of its own.
    /// </summary>
    /// <param name="state">The delivery state of the stream to claim.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns><see langword="true"/> when this replica took the lease; <see langword="false"/> when another replica
    /// took it first.</returns>
    private async ValueTask<bool> TryClaimAsync(
        PersistedAuditStreamState state,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();

        state.LeaseHolder = LeaseHolder;
        state.LeaseExpiryTime = TimeProvider.GetUtcNow() + Options.StreamLeaseDuration;

        try
        {
            await store.UpdateStateAsync(state, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        catch (StoreConcurrencyException)
        {
            // Another replica took the stream first, as the store's token check or the save found.
            return false;
        }

        return true;
    }
}
