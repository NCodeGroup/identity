// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.OpenId.GlobalDirectory</c> package:
/// each attempt to register the cell with the global directory that fails, each pass over the directory's change feed
/// that fails, and each change of the feed that fails to apply.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.CellRegistrationFailed,
        Level = LogLevel.Warning,
        Message = "Registering the cell with the global directory failed on attempt {Attempt}; retrying in {Delay}."
    )]
    internal static partial void CellRegistrationFailed(
        this ILogger logger,
        Exception exception,
        int attempt,
        TimeSpan delay
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryChangeFeedUnavailable,
        Level = LogLevel.Warning,
        Message = "Following the global directory's change feed failed because the directory is unavailable; retrying at the next interval."
    )]
    internal static partial void DirectoryChangeFeedUnavailable(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryChangeFeedPassFailed,
        Level = LogLevel.Error,
        Message = "Following the global directory's change feed failed; retrying at the next interval."
    )]
    internal static partial void DirectoryChangeFeedPassFailed(
        this ILogger logger,
        Exception exception
    );

    [LoggerMessage(
        EventId = EventIds.DirectoryChangeApplyFailed,
        Level = LogLevel.Error,
        Message = "Applying the global directory's change {Sequence} failed; the change feed stops at it and retries at the next interval."
    )]
    internal static partial void DirectoryChangeApplyFailed(
        this ILogger logger,
        Exception exception,
        long sequence
    );
}
