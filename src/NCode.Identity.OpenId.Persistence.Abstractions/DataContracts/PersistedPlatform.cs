// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the data for a persisted platform.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedPlatform : BasePersistedPlatformResource
{
    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceType)]
    public override string ResourceType => ResourceTypePrefix;

    /// <summary>
    /// Gets or sets the JSON settings for the platform.
    /// </summary>
    public required PersistedPlatformSettings Settings { get; set; }

    /// <summary>
    /// Gets or sets the sequence of the directory change whose platform document the platform last mirrored, or
    /// <c>0</c> for a platform that no document wrote.
    /// </summary>
    public long DirectorySequence { get; set; }
}
