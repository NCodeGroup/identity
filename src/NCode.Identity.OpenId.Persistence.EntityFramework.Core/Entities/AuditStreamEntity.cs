// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for an audit stream: a tenant's endpoint that receives the tenant's
/// audit records as they are stored. The stream's signing secrets and delivery state are kept in their own rows. The
/// complimentary DTO for this entity is <c>PersistedAuditStream</c>.
/// </summary>
// See ADR-1534.
[Index(nameof(TenantId), nameof(StreamId), IsUnique = true)]
public sealed class AuditStreamEntity : ISupportTenantEntity, ISupportConcurrencyToken
{
    /// <summary>
    /// Gets the surrogate identifier for this entity.
    /// </summary>
    [Key]
    [SurrogateKey]
    public required Guid Id { get; init; }

    /// <inheritdoc />
    [ForeignKey(nameof(Tenant))]
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets the owning tenant's normalized identifier, denormalized onto this entity so the tenant-scoping global query
    /// filter compares a single indexed column without a navigation join.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string NormalizedTenantId { get; init; }

    /// <summary>
    /// Gets the identifier of the stream, unique within its tenant.
    /// </summary>
    [Unicode(false)]
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
    [Unicode(false)]
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

    //

    /// <inheritdoc />
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    [ConcurrencyCheck]
    public required string ConcurrencyToken { get; set; }

    // navigational properties

    /// <inheritdoc />
    public required TenantEntity Tenant { get; init; }

    /// <summary>
    /// Gets the delivery state of this stream.
    /// </summary>
    public AuditStreamStateEntity? State { get; init; }

    /// <summary>
    /// Gets the collection of secrets that sign this stream's deliveries.
    /// </summary>
    public required IEnumerable<AuditStreamSigningSecretEntity> SigningSecrets { get; init; }
}
