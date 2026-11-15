// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Logging;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Mirrors the global directory's team and platform documents into the cell when the host starts and then every
/// <see cref="DirectoryClientOptions.PollInterval"/>: it reads the highest sequence the cell has applied, pages
/// <see cref="ITeamDirectory.ListChangesAsync"/> until a page is empty, applies each team change through
/// <see cref="ITeamDocumentApplier"/> and each platform change through <see cref="IPlatformDocumentApplier"/>. One
/// replica runs each pass, under a lease on the shared-state port that lasts half the interval. The replica that holds
/// the lease also reconciles every mirrored team's root key for the cell's region through
/// <see cref="ITeamDocumentApplier.ReconcileRootKeysAsync"/> after the change pages, on its first pass and then once
/// <c>RootKeyReconcileInterval</c> has passed, so a key that failed its probe or its rotation cooldown is applied
/// once it can be. A directory that
/// cannot be reached throws <see cref="DirectoryUnavailableException"/>, which is logged and waits for the next
/// interval, and so does any other pass that fails.
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
    IExpiringKeySet expiringKeySet,
    IOptions<CellOptions> cellOptions,
    IOptions<DirectoryClientOptions> directoryClientOptions,
    TimeProvider timeProvider,
    ILogger<DirectoryChangeFeedService> logger
) : BackgroundService
{
    /// <summary>
    /// The key of the lease that one replica takes on each pass.
    /// </summary>
    internal const string LeaseKey = "directory-change-feed";

    /// <summary>
    /// The most changes that one page of the feed holds.
    /// </summary>
    internal const int PageSize = 100;

    /// <summary>
    /// The time that must pass before this replica reconciles the mirrored teams' root keys again.
    /// </summary>
    internal static TimeSpan RootKeyReconcileInterval { get; } = TimeSpan.FromMinutes(15);

    private ITeamDirectory TeamDirectory { get; } = teamDirectory;
    private ITeamDocumentApplier TeamDocumentApplier { get; } = teamDocumentApplier;
    private IPlatformDocumentApplier PlatformDocumentApplier { get; } = platformDocumentApplier;
    private IExpiringKeySet ExpiringKeySet { get; } = expiringKeySet;
    private CellOptions CellOptions { get; } = cellOptions.Value;
    private IOptions<DirectoryClientOptions> Options { get; } = directoryClientOptions;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DirectoryChangeFeedService> Logger { get; } = logger;

    // The time of this replica's last reconcile of the teams' root keys, or null before its first.
    private DateTimeOffset? LastRootKeyReconcileWhen { get; set; }

    // Read on use, never in the constructor, so the options are not built while the container composes this service.
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

    private async ValueTask RunCoreAsync(CancellationToken cancellationToken)
    {
        // The lease lasts half the interval: one that lasted the whole interval would still be held when the same
        // replica's timer ticks next.
        var leaseExpiresWhen = TimeProvider.GetUtcNow() + PollInterval / 2;
        if (!await ExpiringKeySet.TryAddAsync(LeaseKey, leaseExpiresWhen, cancellationToken))
            return;

        var teamSequence = await TeamDocumentApplier.GetAppliedSequenceAsync(cancellationToken);
        var platformSequence = await PlatformDocumentApplier.GetAppliedSequenceAsync(
            cancellationToken
        );
        var after = Math.Max(teamSequence, platformSequence);

        while (true)
        {
            var changes = await TeamDirectory.ListChangesAsync(after, PageSize, cancellationToken);
            if (changes.Count == 0)
                break;

            var previous = after;
            foreach (var change in changes)
            {
                await ApplyAsync(change, cancellationToken);
                after = Math.Max(after, change.Sequence);
            }

            // A page that does not advance the sequence would repeat for ever.
            if (after == previous)
                break;
        }

        await ReconcileRootKeysAsync(cancellationToken);
    }

    // A team's root key is applied while its document is applied, so a key that failed its probe or its rotation
    // cooldown stays unapplied until the directory publishes the team again; the replica that holds the lease
    // reconciles every team's key on its first pass and then once the interval has passed.
    private async ValueTask ReconcileRootKeysAsync(CancellationToken cancellationToken)
    {
        var now = TimeProvider.GetUtcNow();
        if (LastRootKeyReconcileWhen is { } last && now - last < RootKeyReconcileInterval)
            return;

        await TeamDocumentApplier.ReconcileRootKeysAsync(cancellationToken);
        LastRootKeyReconcileWhen = now;
    }

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
