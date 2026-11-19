// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.EndToEnd.TestSupport;

/// <summary>
/// Stops what the end-to-end suites started, running every stop even when one throws.
/// </summary>
public static class Teardown
{
    /// <summary>
    /// Runs every stop in order, even when an earlier one throws, and reports each failure.
    /// </summary>
    /// <param name="stops">Each one stops one thing the suite started.</param>
    /// <remarks>
    /// This method takes no <see cref="CancellationToken"/> because every stop must run.
    /// </remarks>
    /// <exception cref="AggregateException">
    /// A stop failed: the aggregate holds each stop failure, in order.
    /// </exception>
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "Stopping what a suite started is uncancellable: every stop runs, and DisposeAsync takes no token (ADR-1496)."
    )]
    public static async ValueTask StopAllAsync(params Func<ValueTask>[] stops)
    {
        ArgumentNullException.ThrowIfNull(stops);

        List<Exception> stopFailures = [];
        foreach (var stop in stops)
        {
            try
            {
                await stop();
            }
            catch (Exception stopFailure)
            {
                stopFailures.Add(stopFailure);
            }
        }

        if (stopFailures.Count > 0)
        {
            throw new AggregateException(stopFailures);
        }
    }

    /// <summary>
    /// Stops what a failed start already started, and reports the start failure even when a stop also fails.
    /// </summary>
    /// <param name="startFailure">The exception that failed the start.</param>
    /// <param name="stops">Each one stops one thing the start started.</param>
    /// <remarks>
    /// This method takes no <see cref="CancellationToken"/> because every disposal must run.
    /// </remarks>
    /// <exception cref="AggregateException">
    /// A stop failed: the aggregate holds <paramref name="startFailure"/> first, then each stop failure.
    /// </exception>
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "Stopping what a failed start started is uncancellable: every disposal runs, and DisposeAsync takes no token (ADR-1496)."
    )]
    public static async ValueTask StopStartedAsync(
        Exception startFailure,
        params Func<ValueTask>[] stops
    )
    {
        ArgumentNullException.ThrowIfNull(startFailure);

        try
        {
            await StopAllAsync(stops);
        }
        catch (AggregateException stopFailures)
        {
            throw new AggregateException([startFailure, .. stopFailures.InnerExceptions]);
        }
    }
}
