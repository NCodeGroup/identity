// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for a team identifier that the directory reserved, published or
/// retired, with the team document that the control-plane cell published.
/// </summary>
public sealed class TeamRecord
{
    /// <summary>
    /// The state of a team whose identifier is reserved and whose document is not yet published.
    /// </summary>
    public const string StateReserved = "reserved";

    /// <summary>
    /// The state of a team whose document is published.
    /// </summary>
    public const string StatePublished = "published";

    /// <summary>
    /// The state of a team that was deleted. Its identifier is never reissued.
    /// </summary>
    public const string StateRetired = "retired";

    /// <summary>
    /// Gets the surrogate identifier for this record.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets or sets the team identifier.
    /// </summary>
    public required string TeamId { get; set; }

    /// <summary>
    /// Gets or sets the value of <see cref="TeamId"/> normalized so that lookups are case-insensitive.
    /// </summary>
    public required string NormalizedTeamId { get; set; }

    /// <summary>
    /// Gets or sets the normalized identifier of the parent team, or <c>null</c> for a top-level team.
    /// </summary>
    public string? NormalizedParentTeamId { get; set; }

    /// <summary>
    /// Gets or sets the normalized team path: the normalized identifiers of the teams from the top-level team down to
    /// this team, each followed by <c>/</c>, after a leading <c>/</c> (such as <c>/acme/acme-web/</c>). A team and
    /// every team beneath it share the team's path as a prefix. The directory sets it when the team is published, and
    /// re-paths the team and its subtree beneath its parent when the parent is published after it.
    /// </summary>
    // See ADR-1426.
    public required string NormalizedPath { get; set; }

    /// <summary>
    /// Gets or sets the state of the team, one of <see cref="StateReserved"/>, <see cref="StatePublished"/> and
    /// <see cref="StateRetired"/>.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the published team document as its schema-versioned JSON envelope, or <c>null</c> when none is
    /// published.
    /// </summary>
    /// <remarks>
    /// The column keeps the envelope as text, which the store parses only when it reads the document. A load of the row
    /// that never reads the document, such as a publish, a retire or the re-pathing of a subtree, parses nothing, so a
    /// text that does not parse fails only the reads of the document, and the next publish repairs it.
    /// </remarks>
    // See ADR-1411.
    public string? DocumentJson { get; set; }

    /// <summary>
    /// Gets or sets the sequence of the team's latest change in the feed: its publish, a publish of a team above it, or
    /// its retire; <c>0</c> when the team has none.
    /// </summary>
    public long Sequence { get; set; }
}
