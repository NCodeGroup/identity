// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace NCode.Identity.Secrets.GoogleCloudKms.Tests;

/// <summary>
/// A fake time provider that records the due time of each timer that is created, so a test moves the fake time only
/// after the code under test waits on it.
/// </summary>
internal sealed class RecordingTimeProvider : FakeTimeProvider
{
    /// <summary>
    /// Gets the due time of each timer that was created, in creation order.
    /// </summary>
    public ConcurrentQueue<TimeSpan> DueTimes { get; } = new();

    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        DueTimes.Enqueue(dueTime);
        return timer;
    }
}
