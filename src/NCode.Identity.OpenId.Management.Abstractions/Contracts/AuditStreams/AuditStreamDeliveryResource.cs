// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents the delivery state of an audit stream.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class AuditStreamDeliveryResource
{
    /// <summary>
    /// Gets the time of the last successful delivery, or <c>null</c> when none has succeeded.
    /// </summary>
    public DateTimeOffset? LastDeliveryTime { get; init; }

    /// <summary>
    /// Gets the time of the last failed delivery, or <c>null</c> when none has failed.
    /// </summary>
    public DateTimeOffset? LastFailureTime { get; init; }

    /// <summary>
    /// Gets a short code describing the last failure, or <c>null</c> when none has failed.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the number of deliveries that have failed in a row.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Gets the earliest time the next delivery is attempted.
    /// </summary>
    public required DateTimeOffset NextAttemptTime { get; init; }
}
