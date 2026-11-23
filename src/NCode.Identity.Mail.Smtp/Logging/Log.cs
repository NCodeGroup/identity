// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.Mail.Smtp.Logging;

/// <summary>
/// Provides source-generated, strongly-typed log messages for the <c>NCode.Identity.Mail.Smtp</c> package.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(
        EventId = EventIds.SmtpQuitFailed,
        Level = LogLevel.Warning,
        Message = "The SMTP server accepted the message, but closing the connection failed with {ExceptionType}; the message counts as delivered."
    )]
    internal static partial void SmtpQuitFailed(this ILogger logger, string exceptionType);
}
