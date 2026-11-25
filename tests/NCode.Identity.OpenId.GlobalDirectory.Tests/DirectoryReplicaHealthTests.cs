// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryReplicaHealthTests : BaseTests
{
    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private DirectoryReplicaHealth CreateHealth() =>
        new(
            TimeProvider,
            new DirectoryMetrics(new TestMeterFactory()),
            Options.Create(new CellOptions { Name = "cell-a" })
        );

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose() { }
    }

    [Fact]
    public void AgeSeconds_WhenTimePasses_ThenGrows()
    {
        var health = CreateHealth();

        TimeProvider.Advance(TimeSpan.FromSeconds(90));

        Assert.Equal(90, health.AgeSeconds);
    }

    [Fact]
    public void RecordPass_WhenPassCompletes_ThenResetsAgeAndKeepsSequence()
    {
        var health = CreateHealth();
        TimeProvider.Advance(TimeSpan.FromSeconds(90));

        health.RecordPass(42);
        TimeProvider.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(5, health.AgeSeconds);
        Assert.Equal(42, health.Sequence);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenReplicaIsOld_ThenIsHealthyWithData()
    {
        var health = CreateHealth();
        health.RecordPass(7);
        TimeProvider.Advance(TimeSpan.FromHours(2));

        var result = await health.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(7200d, result.Data[DirectoryReplicaHealth.AgeDataKey]);
        Assert.Equal(7L, result.Data[DirectoryReplicaHealth.SequenceDataKey]);
    }
}
