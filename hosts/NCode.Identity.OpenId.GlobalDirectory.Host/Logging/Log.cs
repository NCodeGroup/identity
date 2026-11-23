// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.OpenId.GlobalDirectory.Host</c>
/// host.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.DirectoryMigrateCommandFailed,
        Level = LogLevel.Error,
        Message = "The migrate command failed."
    )]
    internal static partial void DirectoryMigrateCommandFailed(
        this ILogger logger,
        Exception exception
    );
}
