// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the data for a document that the control-plane cell has committed and still has to publish to the global
/// directory.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedDirectoryOutboxEntry
{
    /// <summary>
    /// Gets the position of the entry in the outbox, which only rises.
    /// </summary>
    public required long Sequence { get; init; }

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
    /// Gets the revision of the document.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the JSON content of the document, or <c>null</c> when the document is retired.
    /// </summary>
    public string? ContentJson { get; init; }

    /// <summary>
    /// Gets the instant at which the entry was written.
    /// </summary>
    public required DateTimeOffset CreatedWhen { get; init; }
}
