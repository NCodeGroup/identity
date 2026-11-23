// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents the REST resource for an audit stream, an endpoint of the tenant's own that receives the tenant's audit
/// records as they are stored.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class AuditStreamResource
{
    /// <summary>
    /// Gets the opaque, server-generated public identifier for this audit stream.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the human-readable name for this audit stream.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the HTTPS endpoint that receives the audit records.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// Gets a value indicating whether this audit stream delivers records.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the time this audit stream was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>
    /// Gets the signing secrets that currently sign the deliveries; the secret values are never returned.
    /// </summary>
    public required IReadOnlyList<AuditStreamSigningSecretResource> SigningSecrets { get; init; }

    /// <summary>
    /// Gets the delivery state of this audit stream.
    /// </summary>
    public required AuditStreamDeliveryResource Delivery { get; init; }
}
