// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents a signing secret of an audit stream without its value.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class AuditStreamSigningSecretResource
{
    /// <summary>
    /// Gets the opaque, server-generated public identifier for this signing secret.
    /// </summary>
    public required string SecretId { get; init; }

    /// <summary>
    /// Gets the time this signing secret was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>
    /// Gets the time this signing secret stops signing deliveries, or <c>null</c> when it has no expiry.
    /// </summary>
    public DateTimeOffset? ExpiryTime { get; init; }
}
