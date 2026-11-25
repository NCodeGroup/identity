// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for a management key that the cell refuses. The complimentary DTO for
/// this entity is <see cref="PersistedRevokedManagementKey"/>.
/// </summary>
/// <remarks>
/// This entity is intentionally not tenant-scoped, and a row never changes after it is written.
/// </remarks>
public sealed class RevokedManagementKeyEntity
{
    /// <summary>
    /// Gets the identifier of the revoked key, which is the primary key.
    /// </summary>
    [Key]
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string KeyId { get; init; }

    /// <summary>
    /// Gets the instant at which the key was revoked.
    /// </summary>
    public required DateTimeOffset RevokedWhen { get; init; }

    /// <summary>
    /// Gets where the revocation came from.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceType)]
    public required string Source { get; init; }
}
