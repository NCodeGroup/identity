// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
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
internal sealed class DirectoryOutboxPublisher : BackgroundService
{
    /// <summary>
    /// The name of the meter that holds the publisher's instruments.
    /// </summary>
    internal const string MeterName = "NCode.Identity.OpenId.GlobalDirectory";

    /// <summary>
    /// The name of the counter of the documents that the directory has acknowledged.
    /// </summary>
    internal const string PublishedCounterName = "ncode.identity.directory.outbox.published";

    /// <summary>
    /// The most pending documents that one drain reads.
    /// </summary>
    internal const int PageSize = 100;

    private const string KindAttribute = "ncode.identity.directory.document.kind";

    private IPlatformTopology Topology { get; }
    private IDirectoryOutbox Outbox { get; }
    private IDirectoryOutboxSignal Signal { get; }
    private IReplicatedDocumentDirectory Directory { get; }
    private IOptions<DirectoryClientOptions> Options { get; }
    private TimeProvider TimeProvider { get; }
    private ILogger<DirectoryOutboxPublisher> Logger { get; }
    private Counter<long> Published { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryOutboxPublisher"/> class.
    /// </summary>
    /// <param name="topology">The <see cref="IPlatformTopology"/> that says whether the cell publishes.</param>
    /// <param name="outbox">The <see cref="IDirectoryOutbox"/> that holds the pending documents.</param>
    /// <param name="signal">The <see cref="IDirectoryOutboxSignal"/> that wakes the publisher.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that receives the documents.</param>
    /// <param name="options">The <see cref="DirectoryClientOptions"/> of the directory client.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> that supplies the current time.</param>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> that creates the publisher's meter.</param>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> of the publisher.</param>
    public DirectoryOutboxPublisher(
        IPlatformTopology topology,
        IDirectoryOutbox outbox,
        IDirectoryOutboxSignal signal,
        IReplicatedDocumentDirectory directory,
        IOptions<DirectoryClientOptions> options,
        TimeProvider timeProvider,
        IMeterFactory meterFactory,
        ILogger<DirectoryOutboxPublisher> logger
    )
    {
        Topology = topology;
        Outbox = outbox;
        Signal = signal;
        Directory = directory;
        Options = options;
        TimeProvider = timeProvider;
        Logger = logger;

        var meter = meterFactory.Create(MeterName);
        Published = meter.CreateCounter<long>(
            PublishedCounterName,
            unit: "{document}",
            description: "The documents of the outbox that the global directory acknowledged."
        );
    }

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
    /// one.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    internal async ValueTask RepublishBehindAsync(CancellationToken cancellationToken)
    {
        var remote = new List<ReplicatedDocument>();
        string? continuation = null;
        do
        {
            var page = await Directory.ListRevisionsAsync(
                continuation,
                PageSize,
                cancellationToken
            );
            remote.AddRange(page.Revisions);
            continuation = page.Continuation;
        } while (continuation is not null);

        var local = await Outbox.ListLocalRevisionsAsync(cancellationToken);

        foreach (var document in local)
        {
            var match = remote.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, document.Kind, StringComparison.Ordinal)
                && string.Equals(candidate.Key, document.Key, StringComparison.Ordinal)
            );
            if (match is null || document.Revision >= match.Revision)
                continue;

            var atRevision = match.Revision + 1;
            await Outbox.RepublishAsync(document.Kind, document.Key, atRevision, cancellationToken);
            Logger.DirectoryOutboxRepublished(document.Kind, document.Key, atRevision);
        }
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

            var kind = new KeyValuePair<string, object?>(KindAttribute, document.Kind);
            Published.Add(1, kind);
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
        var through = Math.Max(stored, document.Revision);
        await Outbox.AcknowledgeAsync(document.Kind, document.Key, through, cancellationToken);

        // A higher stored revision means the directory holds a document newer than the local source, as after a
        // restore of the database, so the current local document is enqueued above it and sent in the next drain.
        if (stored > document.Revision)
        {
            var atRevision = stored + 1;
            await Outbox.RepublishAsync(document.Kind, document.Key, atRevision, cancellationToken);
            Logger.DirectoryOutboxRepublished(document.Kind, document.Key, atRevision);
        }
    }
}
