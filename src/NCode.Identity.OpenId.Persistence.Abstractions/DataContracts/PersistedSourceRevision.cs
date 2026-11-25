// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the latest revision that the control-plane cell has given a document that it publishes to the global
/// directory.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedSourceRevision
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
    /// Gets the latest revision of the document.
    /// </summary>
    public required long Revision { get; init; }
}
