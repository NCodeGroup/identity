// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Owns the instruments of the <see cref="MeterName"/> meter, created once through the <see cref="IMeterFactory"/> so
/// each service provider has its own; the outbox publisher and the replica health record through its members and never
/// touch the meter directly.
/// </summary>
// See ADR-1197.
internal sealed class DirectoryMetrics
{
    /// <summary>
    /// The name of the meter that holds the directory client's instruments.
    /// </summary>
    internal const string MeterName = GlobalDirectoryMetrics.MeterName;

    /// <summary>
    /// The name of the counter of the documents that the directory has acknowledged.
    /// </summary>
    internal const string PublishedCounterName = "ncode.identity.directory.outbox.published";

    /// <summary>
    /// The name of the gauge of the seconds since the last completed feed pass.
    /// </summary>
    internal const string AgeGaugeName = "ncode.identity.directory.replica.age";

    /// <summary>
    /// The name of the gauge of the highest feed sequence that the cell has applied.
    /// </summary>
    internal const string SequenceGaugeName = "ncode.identity.directory.replica.sequence";

    /// <summary>
    /// The name of the counter of the calls to the directory that were sent again after a transient failure.
    /// </summary>
    internal const string RetriesCounterName = "ncode.identity.directory.client.retries";

    /// <summary>
    /// The name of the counter of the attempts and calls to the directory that timed out.
    /// </summary>
    internal const string TimeoutsCounterName = "ncode.identity.directory.client.timeouts";

    /// <summary>
    /// The attribute that names the bounded failure of a retried attempt.
    /// </summary>
    internal const string ErrorTypeAttribute = "error.type";

    /// <summary>
    /// The attribute that names the scope of a timeout: <see cref="AttemptTimeoutScope"/> or
    /// <see cref="TotalTimeoutScope"/>.
    /// </summary>
    internal const string TimeoutScopeAttribute = "ncode.identity.directory.client.timeout_scope";

    /// <summary>
    /// The scope of a timeout of one attempt.
    /// </summary>
    internal const string AttemptTimeoutScope = "attempt";

    /// <summary>
    /// The scope of a timeout of a whole call.
    /// </summary>
    internal const string TotalTimeoutScope = "total";

    /// <summary>
    /// The attribute that names the kind of an acknowledged document.
    /// </summary>
    internal const string KindAttribute = "ncode.identity.directory.document.kind";

    /// <summary>
    /// The attribute that names the reporting cell.
    /// </summary>
    internal const string CellNameAttribute = "cell.name";

    private Meter Meter { get; }
    private Counter<long> PublishedCounter { get; }
    private Counter<long> RetriesCounter { get; }
    private Counter<long> TimeoutsCounter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> that creates the <see cref="MeterName"/>
    /// meter.</param>
    public DirectoryMetrics(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(MeterName);
        PublishedCounter = Meter.CreateCounter<long>(
            PublishedCounterName,
            unit: "{document}",
            description: "The documents of the outbox that the global directory acknowledged."
        );
        RetriesCounter = Meter.CreateCounter<long>(
            RetriesCounterName,
            unit: "{retry}",
            description: "The number of times a call to the global directory was sent again after a transient failure, by the failure."
        );
        TimeoutsCounter = Meter.CreateCounter<long>(
            TimeoutsCounterName,
            unit: "{timeout}",
            description: "The number of times an attempt or a whole call to the global directory timed out, by scope."
        );
    }

    /// <summary>
    /// Records that a call to the directory is sent again after a transient failure.
    /// </summary>
    /// <param name="errorType">The bounded failure: the response's status code, <c>timeout</c>, or the
    /// <see cref="HttpRequestError"/> in snake case.</param>
    public void Retried(string errorType)
    {
        var tag = new KeyValuePair<string, object?>(ErrorTypeAttribute, errorType);
        RetriesCounter.Add(1, tag);
    }

    /// <summary>
    /// Records that an attempt, or a whole call, to the directory timed out.
    /// </summary>
    /// <param name="scope">The bounded scope: <see cref="AttemptTimeoutScope"/> or
    /// <see cref="TotalTimeoutScope"/>.</param>
    public void TimedOut(string scope)
    {
        var tag = new KeyValuePair<string, object?>(TimeoutScopeAttribute, scope);
        TimeoutsCounter.Add(1, tag);
    }

    /// <summary>
    /// Counts a document that the directory acknowledged.
    /// </summary>
    /// <param name="kind">The kind of the acknowledged document.</param>
    public void Published(string kind)
    {
        var tag = new KeyValuePair<string, object?>(KindAttribute, kind);
        PublishedCounter.Add(1, tag);
    }

    /// <summary>
    /// Creates the observable gauges of the cell's replica of the directory's documents.
    /// </summary>
    /// <param name="age">The callback that observes the seconds since the last completed feed pass.</param>
    /// <param name="sequence">The callback that observes the highest feed sequence that the cell has applied.</param>
    public void ObserveReplica(Func<Measurement<double>> age, Func<Measurement<long>> sequence)
    {
        Meter.CreateObservableGauge(
            AgeGaugeName,
            age,
            unit: "s",
            description: "The seconds since the cell's replica of the directory's documents last completed a feed pass."
        );
        Meter.CreateObservableGauge(
            SequenceGaugeName,
            sequence,
            unit: "{sequence}",
            description: "The highest feed sequence that the cell's replica has applied."
        );
    }
}
