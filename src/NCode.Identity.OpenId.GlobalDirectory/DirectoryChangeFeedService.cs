// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Logging;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Mirrors the global directory's team and platform documents into the cell when the host starts and then every
/// <see cref="DirectoryClientOptions.PollInterval"/>: it reads the highest sequence the cell has applied from
/// <see cref="IDirectoryFeedCheckpoint"/>, pages <see cref="ITeamDirectory.ListChangesAsync"/> until a page is empty,
/// applies each team change through <see cref="ITeamDocumentApplier"/> and each platform change through
/// <see cref="IPlatformDocumentApplier"/>, and raises the checkpoint to the last change it applied after each page, so
/// a change that leaves no row behind is not listed again. A change that loses a concurrency race is applied again,
/// which reads its rows again, at most <see cref="ConcurrencyAttempts"/> times. One replica runs each pass, under a
/// lease on the shared-state port keyed by the poll window. The replica that holds the lease also reconciles every
/// mirrored team's root key for the cell's region through <see cref="ITeamDocumentApplier.ReconcileRootKeysAsync"/>
/// after the change pages, on its first pass and then once <c>RootKeyReconcileInterval</c> has passed, so a key that
/// failed its probe or its rotation cooldown is applied once it can be. A directory that cannot be reached throws
/// <see cref="DirectoryUnavailableException"/>, which is logged and waits for the next interval, and so does any other
/// pass that fails.
/// </summary>
/// <remarks>
/// The service does nothing when <see cref="CellOptions.IsControlPlane"/> is <c>true</c>, because the control-plane
/// cell owns the team rows and the platform row.
/// </remarks>
// See ADR-1416.
internal sealed class DirectoryChangeFeedService(
    ITeamDirectory teamDirectory,
    ITeamDocumentApplier teamDocumentApplier,
    IPlatformDocumentApplier platformDocumentApplier,
    IDirectoryFeedCheckpoint feedCheckpoint,
    IExpiringKeySet expiringKeySet,
    IOptions<CellOptions> cellOptions,
    IOptions<DirectoryClientOptions> directoryClientOptions,
    TimeProvider timeProvider,
    ILogger<DirectoryChangeFeedService> logger
) : BackgroundService
{
    /// <summary>
    /// The purpose that prefixes the key of the lease that one replica takes for each poll window.
    /// </summary>
    internal const string LeasePurpose = "directory-change-feed";

    /// <summary>
    /// The most changes that one page of the feed holds.
    /// </summary>
    internal const int PageSize = 100;

    /// <summary>
    /// The most times that one change is applied while it keeps losing a concurrency race.
    /// </summary>
    internal const int ConcurrencyAttempts = 3;

    /// <summary>
    /// The time that must pass before this replica reconciles the mirrored teams' root keys again.
    /// </summary>
    internal static TimeSpan RootKeyReconcileInterval { get; } = TimeSpan.FromMinutes(15);

    private ITeamDirectory TeamDirectory { get; } = teamDirectory;
    private ITeamDocumentApplier TeamDocumentApplier { get; } = teamDocumentApplier;
    private IPlatformDocumentApplier PlatformDocumentApplier { get; } = platformDocumentApplier;
    private IDirectoryFeedCheckpoint FeedCheckpoint { get; } = feedCheckpoint;
    private IExpiringKeySet ExpiringKeySet { get; } = expiringKeySet;
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptions;
    private IOptions<DirectoryClientOptions> Options { get; } = directoryClientOptions;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DirectoryChangeFeedService> Logger { get; } = logger;

    // The time of this replica's last reconcile of the teams' root keys, or null before its first.
    private DateTimeOffset? LastRootKeyReconcileWhen { get; set; }

    // Both options are read on use, never in the constructor, so neither is built while the container composes this
    // service.
    private CellOptions CellOptions => CellOptionsAccessor.Value;
    private TimeSpan PollInterval => Options.Value.PollInterval;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (CellOptions.IsControlPlane)
            return;

        try
        {
            await RunAsync(stoppingToken);

            using var timer = new PeriodicTimer(PollInterval, TimeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Runs one pass, when this replica wins the pass's lease. A pass that fails is logged and run again at the next
    /// interval.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    internal async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(cancellationToken);
        }
        catch (DirectoryUnavailableException exception)
        {
            Logger.DirectoryChangeFeedUnavailable(exception);
        }
        // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.DirectoryChangeFeedPassFailed(exception);
        }
    }

    /// <summary>
    /// Runs one pass unless another replica holds the lease of the current poll window: applies the directory's changes
    /// after the highest sequence the cell has applied, page by page, raises the checkpoint to the last change applied
    /// after each page, then reconciles the mirrored teams' root keys when <see cref="RootKeyReconcileInterval"/> has
    /// passed. A change that loses a concurrency race is applied again, up to <see cref="ConcurrencyAttempts"/> times.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask RunCoreAsync(CancellationToken cancellationToken)
    {
        // Replicas that run in one poll window share the key, so one of them runs the pass, and the lease expires at
        // the window's end, so the next window's pass takes a lease of its own.
        var pollInterval = PollInterval;
        var window = TimeProvider.GetUtcNow().UtcTicks / pollInterval.Ticks;
        var windowKey = $"{LeasePurpose}:{window}";
        var leaseExpiresWhen = new DateTimeOffset((window + 1) * pollInterval.Ticks, TimeSpan.Zero);
        if (!await ExpiringKeySet.TryAddAsync(windowKey, leaseExpiresWhen, cancellationToken))
            return;

        var after = await FeedCheckpoint.GetAppliedSequenceAsync(cancellationToken);

        while (true)
        {
            var changes = await TeamDirectory.ListChangesAsync(after, PageSize, cancellationToken);
            if (changes.Count == 0)
                break;

            var previous = after;
            var allApplied = true;
            foreach (var change in changes)
            {
                if (!await TryApplyAsync(change, cancellationToken))
                {
                    allApplied = false;
                    break;
                }

                after = Math.Max(after, change.Sequence);
            }

            // The checkpoint, not the rows, holds the feed's position, because a change can leave no row behind. It is
            // raised also when a change failed part-way, so the next pass starts after the changes that applied.
            if (after > previous)
                await FeedCheckpoint.RecordAppliedSequenceAsync(after, cancellationToken);

            // Changes apply in sequence order, so none after a failed one applies in this pass.
            if (!allApplied)
                break;

            // A page that does not advance the sequence would repeat for ever.
            if (after == previous)
                break;
        }

        await ReconcileRootKeysAsync(cancellationToken);
    }

    /// <summary>
    /// Applies one change of the feed, again while it loses a concurrency race, up to <see cref="ConcurrencyAttempts"/>
    /// times. A change that fails is logged and ends the pass's changes, but not the pass: the root keys are still
    /// reconciled.
    /// </summary>
    /// <param name="change">The <see cref="DirectoryChange"/> to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when the change was applied, or <see langword="false"/> when it failed and the pass
    /// applies no change after it.</returns>
    private async ValueTask<bool> TryApplyAsync(
        DirectoryChange change,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ApplyAsync(change, cancellationToken);
                return true;
            }
            // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Each attempt goes through the applier, which opens a new store manager and reads the rows again.
                if (exception is StoreConcurrencyException && attempt < ConcurrencyAttempts)
                    continue;

                Logger.DirectoryChangeApplyFailed(exception, change.Sequence);
                return false;
            }
        }
    }

    /// <summary>
    /// Reconciles every mirrored team's root key for the cell's region on this replica's first pass and then once
    /// <see cref="RootKeyReconcileInterval"/> has passed. A team's root key is applied while its document is applied,
    /// so a key that failed its probe or its rotation cooldown stays unapplied until the directory publishes the team
    /// again or this reconcile applies it.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask ReconcileRootKeysAsync(CancellationToken cancellationToken)
    {
        var now = TimeProvider.GetUtcNow();
        if (LastRootKeyReconcileWhen is { } last && now - last < RootKeyReconcileInterval)
            return;

        await TeamDocumentApplier.ReconcileRootKeysAsync(cancellationToken);
        LastRootKeyReconcileWhen = now;
    }

    /// <summary>
    /// Applies one change of the feed: a team change with a document applies the team's document, a team change without
    /// one retires the team, and a platform change applies the platform's document.
    /// </summary>
    /// <param name="change">The <see cref="DirectoryChange"/> to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask ApplyAsync(DirectoryChange change, CancellationToken cancellationToken)
    {
        if (change.TeamId is not null)
        {
            if (change.Document is null)
                await TeamDocumentApplier.RetireAsync(
                    change.TeamId,
                    change.Sequence,
                    cancellationToken
                );
            else
                await TeamDocumentApplier.ApplyAsync(change.Document, cancellationToken);
        }
        else if (change.Platform is not null)
        {
            await PlatformDocumentApplier.ApplyAsync(change.Platform, cancellationToken);
        }
    }
}
