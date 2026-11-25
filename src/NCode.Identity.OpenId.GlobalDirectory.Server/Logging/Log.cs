// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.OpenId.GlobalDirectory.Server</c>
/// package: each client assertion that the directory's token endpoint refuses.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.ClientAssertionRefused,
        Level = LogLevel.Information,
        Message = "The directory's token endpoint refused the client assertion of cell '{CellName}': {Reason}"
    )]
    internal static partial void ClientAssertionRefused(
        this ILogger logger,
        string cellName,
        string reason,
        Exception? exception
    );
}
