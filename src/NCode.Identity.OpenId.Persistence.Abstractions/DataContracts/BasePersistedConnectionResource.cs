// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Provides the base implementation for a persisted connection resource using the
/// <see cref="ISupportPersistedConnectionResource"/> abstraction.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public abstract class BasePersistedConnectionResource : ISupportPersistedConnectionResource
{
    /// <summary>
    /// Gets the prefix for the resource type.
    /// </summary>
    [MaxLength(MaxLengths.ResourceType)]
    protected const string ResourceTypePrefix = OpenIdResourceTypes.Connection;

    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceType)]
    public abstract string ResourceType { get; }

    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceId)]
    public virtual string ResourceId => ConnectionId;

    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceId)]
    public required string ConnectionId { get; init; }

    /// <inheritdoc/>
    [MaxLength(MaxLengths.ConcurrencyToken)]
    public string ConcurrencyToken { get; set; } = string.Empty;

    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceId)]
    public required string TenantId { get; init; }
}
