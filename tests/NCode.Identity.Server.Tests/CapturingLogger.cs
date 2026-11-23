// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Identity.Server.Tests;

/// <summary>
/// Provides an <see cref="ILogger"/> that records every entry as a <see cref="CapturedLog"/>.
/// </summary>
/// <param name="logs">The list that receives each entry.</param>
internal sealed class CapturingLogger(List<CapturedLog> logs) : ILogger
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => logs.Add(new CapturedLog(eventId, formatter(state, exception), exception));
}
