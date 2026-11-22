// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.Mail.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.Mail</c> package.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.MailDeliveryFailed,
        Level = LogLevel.Warning,
        Message = "A mail message could not be delivered on attempt {Attempt} of {MaxAttempts}."
    )]
    public static partial void MailDeliveryFailed(
        this ILogger logger,
        Exception exception,
        int attempt,
        int maxAttempts
    );
}
