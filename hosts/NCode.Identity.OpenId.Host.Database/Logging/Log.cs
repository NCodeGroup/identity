// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.OpenId.Host.Database.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.OpenId.Host.Database</c> project.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.RuntimeRolePostureViolated,
        Level = LogLevel.Warning,
        Message = "The runtime database role is not subject to row-level security: {Violations}. {Remedy}"
    )]
    internal static partial void RuntimeRolePostureViolated(
        this ILogger logger,
        string violations,
        string remedy
    );
}
