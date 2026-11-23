// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains an audit stream as the store keeps it: a tenant's endpoint that receives the tenant's audit records as they
/// are stored, with the secrets that sign the deliveries and the stream's delivery state.
/// </summary>
// See ADR-1534.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedAuditStream
{
    /// <summary>
    /// Gets the identifier of the tenant the stream belongs to.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the identifier of the stream, unique within its tenant.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets or sets the human-readable name of the stream.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.DisplayName)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the URL that receives the deliveries.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.UrlValue)]
    public required string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the stream delivers.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>
    /// Gets the time at which the stream was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>
    /// Gets or sets the concurrency token of the stream, which an update checks.
    /// </summary>
    public required string ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the secrets that sign the stream's deliveries.
    /// </summary>
    public required IReadOnlyList<PersistedAuditStreamSigningSecret> SigningSecrets { get; set; }

    /// <summary>
    /// Gets the stream's delivery state, read with the stream; <see langword="null"/> on a stream that is not yet
    /// stored.
    /// </summary>
    public PersistedAuditStreamState? State { get; init; }
}
