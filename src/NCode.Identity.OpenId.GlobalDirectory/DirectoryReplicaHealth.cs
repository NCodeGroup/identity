// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reports how old the cell's replica of the directory's documents is. It keeps the time of the last feed pass that
/// completed, an empty page included, and the highest sequence that pass applied, and it observes them as the gauges
/// <c>ncode.identity.directory.replica.age</c> and <c>ncode.identity.directory.replica.sequence</c>. As a readiness
/// check it always answers <see cref="HealthStatus.Healthy"/>, because a replica that serves its last known documents
/// is ready; the data of the result carries the age and the sequence for an operator.
/// </summary>
internal sealed class DirectoryReplicaHealth : IHealthCheck
{
    /// <summary>
    /// The name of the meter that holds the replica's instruments, shared with the outbox publisher's.
    /// </summary>
    internal const string MeterName = DirectoryOutboxPublisher.MeterName;

    /// <summary>
    /// The name of the gauge of the seconds since the last completed feed pass.
    /// </summary>
    internal const string AgeGaugeName = "ncode.identity.directory.replica.age";

    /// <summary>
    /// The name of the gauge of the highest feed sequence that the cell has applied.
    /// </summary>
    internal const string SequenceGaugeName = "ncode.identity.directory.replica.sequence";

    /// <summary>
    /// The key of the health data that holds the seconds since the last completed feed pass.
    /// </summary>
    internal const string AgeDataKey = "replicaAgeSeconds";

    /// <summary>
    /// The key of the health data that holds the highest feed sequence that the cell has applied.
    /// </summary>
    internal const string SequenceDataKey = "appliedSequence";

    /// <summary>
    /// The name of the readiness check.
    /// </summary>
    internal const string HealthCheckName = "directory-replica";

    private const string CellNameAttribute = "cell.name";

    private TimeProvider TimeProvider { get; }
    private IOptions<CellOptions> CellOptionsAccessor { get; }

    // The time of the last completed pass, or of this object's creation before the first.
    private long _lastPassTicks;
    private long _appliedSequence;

    public DirectoryReplicaHealth(
        TimeProvider timeProvider,
        IMeterFactory meterFactory,
        IOptions<CellOptions> cellOptions
    )
    {
        TimeProvider = timeProvider;
        CellOptionsAccessor = cellOptions;
        _lastPassTicks = timeProvider.GetUtcNow().UtcTicks;

        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge(
            AgeGaugeName,
            ObserveAge,
            unit: "s",
            description: "The seconds since the cell's replica of the directory's documents last completed a feed pass."
        );
        meter.CreateObservableGauge(
            SequenceGaugeName,
            ObserveSequence,
            unit: "{sequence}",
            description: "The highest feed sequence that the cell's replica has applied."
        );
    }

    /// <summary>
    /// Gets the seconds since the last completed feed pass, or since this object was created before the first.
    /// </summary>
    internal double AgeSeconds
    {
        get
        {
            var lastTicks = Interlocked.Read(ref _lastPassTicks);
            var last = new DateTimeOffset(lastTicks, TimeSpan.Zero);
            var age = TimeProvider.GetUtcNow() - last;
            return age < TimeSpan.Zero ? 0 : age.TotalSeconds;
        }
    }

    /// <summary>
    /// Gets the highest feed sequence that the cell has applied.
    /// </summary>
    internal long Sequence => Interlocked.Read(ref _appliedSequence);

    /// <summary>
    /// Records a completed feed pass.
    /// </summary>
    /// <param name="appliedSequence">The highest feed sequence that the cell has applied.</param>
    internal void RecordPass(long appliedSequence)
    {
        Interlocked.Exchange(ref _appliedSequence, appliedSequence);
        var nowTicks = TimeProvider.GetUtcNow().UtcTicks;
        Interlocked.Exchange(ref _lastPassTicks, nowTicks);
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var data = new Dictionary<string, object>
        {
            [AgeDataKey] = AgeSeconds,
            [SequenceDataKey] = Sequence,
        };
        var result = HealthCheckResult.Healthy(data: data);
        return Task.FromResult(result);
    }

    private Measurement<double> ObserveAge()
    {
        var tag = CellTag();
        return new Measurement<double>(AgeSeconds, tag);
    }

    private Measurement<long> ObserveSequence()
    {
        var tag = CellTag();
        return new Measurement<long>(Sequence, tag);
    }

    private KeyValuePair<string, object?> CellTag() =>
        new(CellNameAttribute, CellOptionsAccessor.Value.Name);
}
