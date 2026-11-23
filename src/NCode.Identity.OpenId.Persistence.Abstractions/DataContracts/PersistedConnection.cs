// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the data for a persisted connection: a tenant-scoped, named resource of a registered strategy through
/// which end users sign in.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedConnection : BasePersistedConnectionResource
{
    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceType)]
    public override string ResourceType => ResourceTypePrefix;

    //

    /// <summary>
    /// Gets the name of this connection, which is unique per tenant (compared case-insensitively) and immutable.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.ConnectionName)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the strategy of this connection, which is immutable.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.ConnectionStrategy)]
    public required string Strategy { get; init; }

    /// <summary>
    /// Gets or sets the name that the sign-in picker shows for this connection.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.DisplayName)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this connection is enabled.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the JSON object of strategy options, which the strategy validates.
    /// </summary>
    public required JsonElement Options { get; set; }

    /// <summary>
    /// Gets or sets the settings document of this connection.
    /// </summary>
    public required PersistedConnectionSettings Settings { get; set; }
}
