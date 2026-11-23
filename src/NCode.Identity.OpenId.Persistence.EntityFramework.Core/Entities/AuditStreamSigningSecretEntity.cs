// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for a secret that signs the deliveries of an
/// <see cref="AuditStreamEntity"/>. The secret's value is protected under the owning tenant's master key. The
/// complimentary DTO for this entity is <c>PersistedAuditStreamSigningSecret</c>.
/// </summary>
// See ADR-1534.
[Index(nameof(AuditStreamId), nameof(SecretId), IsUnique = true)]
public sealed class AuditStreamSigningSecretEntity : ISupportTenantEntity, ISupportConcurrencyToken
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
    /// Gets the foreign key for the stream this secret signs for.
    /// </summary>
    [ForeignKey(nameof(AuditStream))]
    public required Guid AuditStreamId { get; init; }

    /// <summary>
    /// Gets the identifier of the secret, unique within its stream.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string SecretId { get; init; }

    /// <summary>
    /// Gets or sets the secret's value, protected under the tenant's master key.
    /// </summary>
    [Unicode(false)]
    [MaxLength(SecretMaxLengths.EncodedValue)]
    public required string ProtectedValue { get; set; }

    /// <summary>
    /// Gets the time at which the secret was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>
    /// Gets or sets the time after which the stream no longer signs with the secret, or <see langword="null"/> while
    /// the secret is the stream's current one.
    /// </summary>
    public DateTimeOffset? ExpiryTime { get; set; }

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
    /// Gets the navigation property for the stream this secret signs for.
    /// </summary>
    public required AuditStreamEntity AuditStream { get; init; }
}
