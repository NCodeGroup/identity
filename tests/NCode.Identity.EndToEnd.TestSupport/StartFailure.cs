// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.EndToEnd.TestSupport;

/// <summary>
/// Stops what a failed start already started without hiding the start failure.
/// </summary>
public static class StartFailure
{
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
        Justification = "Stopping what a failed start started is uncancellable: every disposal runs, and DisposeAsync takes no token."
    )]
    public static async ValueTask StopStartedAsync(
        Exception startFailure,
        params Func<ValueTask>[] stops
    )
    {
        ArgumentNullException.ThrowIfNull(startFailure);
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
            throw new AggregateException([startFailure, .. stopFailures]);
        }
    }
}
