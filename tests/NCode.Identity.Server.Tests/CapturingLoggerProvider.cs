// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Identity.Server.Tests;

/// <summary>
/// Provides an <see cref="ILoggerProvider"/> whose loggers record every entry as a <see cref="CapturedLog"/>.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// Gets the entries that the loggers of this provider recorded.
    /// </summary>
    public List<CapturedLog> Logs { get; } = [];

    /// <summary>
    /// Gets the formatted messages of the recorded entries.
    /// </summary>
    public IEnumerable<string> Messages => Logs.Select(x => x.Message);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Logs);

    /// <inheritdoc />
    public void Dispose() { }
}
