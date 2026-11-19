// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.EndToEnd.TestSupport.Tests;

public sealed class TeardownTests
{
    private static Func<ValueTask> Recording(List<string> stopped, string name) =>
        () =>
        {
            stopped.Add(name);
            return ValueTask.CompletedTask;
        };

    #region StopAllAsync Tests

    [Fact]
    public async Task StopAllAsync_WhenEveryStopSucceeds_RunsEachInOrder()
    {
        List<string> stopped = [];

        var first = Recording(stopped, "first");
        var second = Recording(stopped, "second");

        await Teardown.StopAllAsync(first, second);

        Assert.Equal(["first", "second"], stopped);
    }

    [Fact]
    public async Task StopAllAsync_WhenAStopFails_RunsTheStopsAfterItAndReportsEachFailureInOrder()
    {
        List<string> stopped = [];
        var firstFailure = new InvalidOperationException("first");
        var secondFailure = new InvalidOperationException("second");
        var afterFirstFailure = Recording(stopped, "after the first failure");

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await Teardown.StopAllAsync(
                () => ValueTask.FromException(firstFailure),
                afterFirstFailure,
                () => ValueTask.FromException(secondFailure)
            )
        );

        Assert.Equal(["after the first failure"], stopped);
        Assert.Equal<Exception>([firstFailure, secondFailure], failure.InnerExceptions);
    }

    #endregion

    #region StopStartedAsync Tests

    [Fact]
    public async Task StopStartedAsync_WhenEveryStopSucceeds_ReturnsSoTheCallerRethrowsTheStartFailure()
    {
        List<string> stopped = [];
        var startFailure = new InvalidOperationException("start");

        var started = Recording(stopped, "started");

        await Teardown.StopStartedAsync(startFailure, started);

        Assert.Equal(["started"], stopped);
    }

    [Fact]
    public async Task StopStartedAsync_WhenAStopFails_ReportsTheStartFailureFirst()
    {
        List<string> stopped = [];
        var startFailure = new InvalidOperationException("start");
        var stopFailure = new InvalidOperationException("stop");
        var afterStopFailure = Recording(stopped, "after the stop failure");

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await Teardown.StopStartedAsync(
                startFailure,
                () => ValueTask.FromException(stopFailure),
                afterStopFailure
            )
        );

        Assert.Equal(["after the stop failure"], stopped);
        Assert.Equal<Exception>([startFailure, stopFailure], failure.InnerExceptions);
    }

    #endregion
}
