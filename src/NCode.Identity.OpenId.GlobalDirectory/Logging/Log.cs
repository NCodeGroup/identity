// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.OpenId.GlobalDirectory</c>
/// package: each attempt to register the cell with the global directory that fails.
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
}
