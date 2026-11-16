// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for the platform document that the control-plane cell published. The
/// table holds at most one row.
/// </summary>
public sealed class PlatformRecord
{
    /// <summary>
    /// The surrogate identifier of the single row.
    /// </summary>
    public const long SingletonId = 1;

    /// <summary>
    /// Gets the surrogate identifier for this record, which is <see cref="SingletonId"/>.
    /// </summary>
    public long Id { get; init; } = SingletonId;

    /// <summary>
    /// Gets or sets the published platform document as its schema-versioned JSON envelope.
    /// </summary>
    /// <remarks>
    /// The store serializes the whole <c>PlatformDocument</c> into this text and deserializes it whole, and
    /// nothing reads its members as JSON, so the column keeps the text the store wrote; a <c>JsonElement</c> would
    /// parse every row that is loaded, including the loads that never read the document.
    /// </remarks>
    public required string DocumentJson { get; set; }

    /// <summary>
    /// Gets or sets the sequence that the directory assigned to the published document.
    /// </summary>
    public long Sequence { get; set; }
}
