// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Names the meter through which the directory client records its instruments, so a host can subscribe to it, for
/// example with OpenTelemetry's <c>AddMeter</c>.
/// </summary>
// See ADR-1536.
[PublicAPI]
public static class GlobalDirectoryMetrics
{
    /// <summary>
    /// The name of the meter that records <c>ncode.identity.directory.outbox.published</c>,
    /// <c>ncode.identity.directory.replica.age</c>, <c>ncode.identity.directory.replica.sequence</c>,
    /// <c>ncode.identity.directory.client.retries</c> and <c>ncode.identity.directory.client.timeouts</c>.
    /// </summary>
    public const string MeterName = "NCode.Identity.OpenId.GlobalDirectory";
}
