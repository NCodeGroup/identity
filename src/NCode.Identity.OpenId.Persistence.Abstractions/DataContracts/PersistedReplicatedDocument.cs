// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the data for a document that the cell keeps a replica of, such as the platform's authority or its
/// management keys. A replica is identified by its <see cref="Kind"/> and <see cref="Key"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedReplicatedDocument
{
    /// <summary>
    /// Gets the kind of the document, such as <c>platform-authority</c>.
    /// </summary>
    [MaxLength(MaxLengths.ResourceType)]
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the key of the document within its <see cref="Kind"/>, which is empty for a document that has one per
    /// platform.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the revision of the document, which only rises.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the JSON content of the document, or <c>null</c> when the document is retired.
    /// </summary>
    public string? ContentJson { get; init; }
}
