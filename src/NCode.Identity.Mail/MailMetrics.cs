// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Names the meter through which the mail library records its instruments, so a host can subscribe to it, for example
/// with OpenTelemetry's <c>AddMeter</c>.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class MailMetrics
{
    /// <summary>
    /// The name of the meter that records <c>ncode.identity.mail.messages</c>.
    /// </summary>
    public const string MeterName = "NCode.Identity.Mail";
}
