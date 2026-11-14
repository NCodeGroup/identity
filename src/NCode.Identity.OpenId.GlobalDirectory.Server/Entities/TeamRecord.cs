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
    /// Gets or sets the state of the team, one of <see cref="StateReserved"/>, <see cref="StatePublished"/> and
    /// <see cref="StateRetired"/>.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the JSON of the published team document, or <c>null</c> when none is published.
    /// </summary>
    public string? DocumentJson { get; set; }

    /// <summary>
    /// Gets or sets the sequence that the directory assigned to the published document, or <c>0</c> when none is
    /// published.
    /// </summary>
    public long Sequence { get; set; }
}
