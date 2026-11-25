// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Publishes the documents of the control-plane cell's outbox to the global directory. It does nothing on a cell that
/// is not the control-plane cell. When the host starts, it republishes through <see cref="IDirectoryOutbox"/> every
/// document whose local revision is lower than the directory's, at the directory's revision plus one, so a restored
/// database catches up. It then drains the outbox: it sends the newest pending revision of each document, in revision
/// order, and acknowledges the documents up to the revision the directory stores. It waits on
/// <see cref="IDirectoryOutboxSignal"/> for <see cref="DirectoryClientOptions.PublishInterval"/> when nothing is
/// pending, and a directory that cannot be reached is retried with the capped exponential backoff of the cell
/// registration.
/// </summary>
/// <param name="topology">The <see cref="IPlatformTopology"/> that says whether the cell publishes.</param>
/// <param name="outbox">The <see cref="IDirectoryOutbox"/> that holds the pending documents.</param>
/// <param name="signal">The <see cref="IDirectoryOutboxSignal"/> that wakes the publisher.</param>
/// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that receives the documents.</param>
/// <param name="options">The <see cref="DirectoryClientOptions"/> of the directory client.</param>
/// <param name="timeProvider">The <see cref="TimeProvider"/> that supplies the current time.</param>
/// <param name="metrics">The <see cref="DirectoryMetrics"/> that count the acknowledged documents.</param>
/// <param name="logger">The <see cref="ILogger{TCategoryName}"/> of the publisher.</param>
internal sealed class DirectoryOutboxPublisher(
    IPlatformTopology topology,
    IDirectoryOutbox outbox,
    IDirectoryOutboxSignal signal,
    IReplicatedDocumentDirectory directory,
    IOptions<DirectoryClientOptions> options,
    TimeProvider timeProvider,
    DirectoryMetrics metrics,
    ILogger<DirectoryOutboxPublisher> logger
) : BackgroundService
{
    /// <summary>
    /// The most pending documents that one drain reads.
    /// </summary>
    internal const int PageSize = 100;

    private IPlatformTopology Topology { get; } = topology;
    private IDirectoryOutbox Outbox { get; } = outbox;
    private IDirectoryOutboxSignal Signal { get; } = signal;
    private IReplicatedDocumentDirectory Directory { get; } = directory;
    private IOptions<DirectoryClientOptions> Options { get; } = options;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DirectoryOutboxPublisher> Logger { get; } = logger;
    private DirectoryMetrics Metrics { get; } = metrics;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Topology.IsControlPlaneCell)
            return;

        try
        {
            var started = false;
            var failures = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!started)
                    {
                        await RepublishBehindAsync(stoppingToken);
                        started = true;
                    }

                    var sent = await DrainAsync(stoppingToken);
                    failures = 0;
                    if (sent == 0)
                    {
                        var interval = Options.Value.PublishInterval;
                        await Signal.WaitAsync(interval, stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    failures++;
                    var jitter = Random.Shared.NextDouble();
                    var delay = CellRegistrationService.GetDelay(failures, jitter);
                    if (exception is DirectoryUnavailableException)
                        Logger.DirectoryOutboxUnavailable(exception, failures, delay);
                    else
                        Logger.DirectoryOutboxPublishFailed(exception, failures, delay);

                    await Task.Delay(delay, TimeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Republishes every document whose local revision is lower than the directory's, at the directory's revision plus
    /// one. A document whose republication fails does not stop the documents after it; the pass then throws the first
    /// failure.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    internal async ValueTask RepublishBehindAsync(CancellationToken cancellationToken)
    {
        var remote = new Dictionary<(string Kind, string Key), long>();
        string? continuation = null;
        do
        {
            var page = await Directory.ListRevisionsAsync(
                continuation,
                PageSize,
                cancellationToken
            );
            foreach (var revision in page.Revisions)
                remote[(revision.Kind, revision.Key)] = revision.Revision;

            continuation = page.Continuation;
        } while (continuation is not null);

        var local = await Outbox.ListLocalRevisionsAsync(cancellationToken);

        ExceptionDispatchInfo? firstFailure = null;
        foreach (var document in local)
        {
            if (!remote.TryGetValue((document.Kind, document.Key), out var remoteRevision))
                continue;

            if (document.Revision >= remoteRevision)
                continue;

            var atRevision = remoteRevision + 1;
            try
            {
                await Outbox.RepublishAsync(
                    document.Kind,
                    document.Key,
                    atRevision,
                    cancellationToken
                );
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The documents after it are still republished, and the pass reports itself incomplete below.
                Logger.DirectoryOutboxRepublishFailed(exception, document.Kind, document.Key);
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                continue;
            }

            Logger.DirectoryOutboxRepublished(document.Kind, document.Key, atRevision);
        }

        // The pass is incomplete, so the publisher's backoff retries it.
        firstFailure?.Throw();
    }

    /// <summary>
    /// Sends the newest pending revision of each document and acknowledges each document through the revision the
    /// directory stores.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// number of documents sent. A document whose publication fails stays pending and does not stop the documents after
    /// it; the pass then throws the first failure.</returns>
    internal async ValueTask<int> DrainAsync(CancellationToken cancellationToken)
    {
        var pending = await Outbox.ListPendingAsync(PageSize, cancellationToken);
        var newest = pending
            .GroupBy(document => (document.Kind, document.Key))
            .Select(group => group.OrderByDescending(document => document.Revision).First())
            .ToList();

        var sent = 0;
        ExceptionDispatchInfo? firstFailure = null;
        foreach (var document in newest)
        {
            try
            {
                await PublishOneAsync(document, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The row stays pending for the next drain, and the documents after it are still sent.
                Logger.DirectoryOutboxDocumentFailed(exception, document.Kind, document.Key);
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                continue;
            }

            Metrics.Published(document.Kind);
            sent++;
        }

        // The pass reports its failure after every document had its turn, so the publisher's backoff applies.
        firstFailure?.Throw();

        return sent;
    }

    /// <summary>
    /// Publishes one pending document, acknowledges it through the revision the directory stores, and enqueues the
    /// current local document above that revision when the directory holds a newer one.
    /// </summary>
    /// <param name="document">The pending document to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask PublishOneAsync(
        ReplicatedDocument document,
        CancellationToken cancellationToken
    )
    {
        var stored = await Directory.PublishAsync(document, cancellationToken);

        // A higher stored revision means the directory holds a document newer than the local source, as after a
        // restore of the database, so the current local document is enqueued above it and sent in the next drain.
        // The enqueue comes before the acknowledgement, so a failure leaves the pending row in place.
        if (stored > document.Revision)
        {
            var atRevision = stored + 1;
            await Outbox.RepublishAsync(document.Kind, document.Key, atRevision, cancellationToken);
            Logger.DirectoryOutboxRepublished(document.Kind, document.Key, atRevision);
        }

        var through = Math.Max(stored, document.Revision);
        await Outbox.AcknowledgeAsync(document.Kind, document.Key, through, cancellationToken);
    }
}
