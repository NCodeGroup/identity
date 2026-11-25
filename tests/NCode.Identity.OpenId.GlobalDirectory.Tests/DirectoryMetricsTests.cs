// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryMetricsTests : BaseTests
{
    private ServiceProvider Provider { get; }
    private IMeterFactory MeterFactory { get; }
    private DirectoryMetrics Metrics { get; }

    public DirectoryMetricsTests()
    {
        Provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        MeterFactory = Provider.GetRequiredService<IMeterFactory>();
        Metrics = new DirectoryMetrics(MeterFactory);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Provider.Dispose();

        base.Dispose(disposing);
    }

    [Fact]
    public void Published_WhenCalled_CountsOneDocumentWithItsKind()
    {
        using var collector = new MetricCollector<long>(
            MeterFactory,
            DirectoryMetrics.MeterName,
            DirectoryMetrics.PublishedCounterName
        );

        Metrics.Published(ReplicatedDocumentKinds.ManagementKeys);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            ReplicatedDocumentKinds.ManagementKeys,
            measurement.Tags[DirectoryMetrics.KindAttribute]
        );
    }

    [Fact]
    public void ObserveReplica_WhenObserved_ReportsAgeAndSequenceWithTheCellName()
    {
        using var ageCollector = new MetricCollector<double>(
            MeterFactory,
            DirectoryMetrics.MeterName,
            DirectoryMetrics.AgeGaugeName
        );
        using var sequenceCollector = new MetricCollector<long>(
            MeterFactory,
            DirectoryMetrics.MeterName,
            DirectoryMetrics.SequenceGaugeName
        );
        var tag = new KeyValuePair<string, object?>(DirectoryMetrics.CellNameAttribute, "cell-a");
        Metrics.ObserveReplica(
            () => new Measurement<double>(12.5, tag),
            () => new Measurement<long>(42, tag)
        );

        ageCollector.RecordObservableInstruments();
        sequenceCollector.RecordObservableInstruments();

        var age = Assert.Single(ageCollector.GetMeasurementSnapshot());
        Assert.Equal(12.5, age.Value);
        Assert.Equal("cell-a", age.Tags[DirectoryMetrics.CellNameAttribute]);
        var sequence = Assert.Single(sequenceCollector.GetMeasurementSnapshot());
        Assert.Equal(42, sequence.Value);
        Assert.Equal("cell-a", sequence.Tags[DirectoryMetrics.CellNameAttribute]);
    }
}
