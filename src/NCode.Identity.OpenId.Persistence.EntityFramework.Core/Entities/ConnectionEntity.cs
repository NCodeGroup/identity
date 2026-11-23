// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for a connection: a tenant-scoped, named resource of a registered
/// strategy that signs end users in. The complimentary DTO for this entity is <c>PersistedConnection</c>.
/// </summary>
[Index(nameof(TenantId), nameof(NormalizedConnectionId), IsUnique = true)]
[Index(nameof(TenantId), nameof(NormalizedName), IsUnique = true)]
public sealed class ConnectionEntity : ISupportTenantEntity, ISupportConcurrencyToken
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
    /// Gets the owning tenant's normalized identifier, denormalized onto this entity so the tenant-scoping global
    /// query filter compares a single indexed column without a navigation join.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string NormalizedTenantId { get; init; }

    /// <summary>
    /// Gets the opaque, server-generated public identifier for this connection.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets the normalized public identifier of this connection, which is unique within the tenant.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string NormalizedConnectionId { get; init; }

    /// <summary>
    /// Gets the name of this connection, which is unique within the tenant and never changes.
    /// </summary>
    [Unicode(false)]
    [MaxLength(OpenIdMaxLengths.ConnectionName)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the normalized name of this connection, which is unique within the tenant.
    /// </summary>
    [Unicode(false)]
    [MaxLength(OpenIdMaxLengths.ConnectionName)]
    public required string NormalizedName { get; init; }

    /// <summary>
    /// Gets the name of the strategy that this connection uses, which never changes.
    /// </summary>
    [Unicode(false)]
    [MaxLength(OpenIdMaxLengths.ConnectionStrategy)]
    public required string Strategy { get; init; }

    /// <summary>
    /// Gets or sets the name that the sign-in picker shows.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.DisplayName)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this connection signs users in.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the strategy's options document.
    /// </summary>
    public required JsonElement OptionsJson { get; set; }

    /// <summary>
    /// Gets or sets the <c>connection</c>-scope settings document.
    /// </summary>
    public required JsonElement SettingsJson { get; set; }

    //

    /// <inheritdoc />
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    [ConcurrencyCheck]
    public required string ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token of the settings sub-resource.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    public required string SettingsConcurrencyToken { get; set; }

    // navigational properties

    /// <summary>
    /// Gets the owning tenant.
    /// </summary>
    public required TenantEntity Tenant { get; init; }
}
