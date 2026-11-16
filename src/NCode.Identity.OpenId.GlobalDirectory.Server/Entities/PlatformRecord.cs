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
    /// The column keeps the envelope as text, which the store parses only when it reads the document. A publish loads
    /// the row without reading the document and parses nothing, so a text that does not parse fails only the reads of
    /// the document, and the next publish repairs it.
    /// </remarks>
    // See ADR-1411.
    public required string DocumentJson { get; set; }

    /// <summary>
    /// Gets or sets the sequence that the directory assigned to the published document.
    /// </summary>
    public long Sequence { get; set; }
}
