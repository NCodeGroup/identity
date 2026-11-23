// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Identity.Server.Tests;

/// <summary>
/// Represents one log entry that a <see cref="CapturingLogger"/> recorded.
/// </summary>
/// <param name="EventId">The event ID of the entry.</param>
/// <param name="Message">The formatted message of the entry.</param>
/// <param name="Exception">The exception that the entry carries, if any.</param>
internal sealed record CapturedLog(EventId EventId, string Message, Exception? Exception);
