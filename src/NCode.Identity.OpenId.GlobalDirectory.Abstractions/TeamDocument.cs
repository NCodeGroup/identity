// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Json;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes a team as the control-plane cell publishes it and every other cell mirrors it.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TeamDocument
{
    /// <summary>
    /// Gets the identifier of the team.
    /// </summary>
    public required string TeamId { get; init; }

    /// <summary>
    /// Gets the display name of the team.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the team is disabled, so that its tenants answer as disabled in every cell.
    /// </summary>
    public required bool IsDisabled { get; init; }

    /// <summary>
    /// Gets the identifier of the parent team, or <c>null</c> for a top-level team.
    /// </summary>
    public string? ParentTeamId { get; init; }

    /// <summary>
    /// Gets the team's settings, a JSON object, which is <see cref="JsonElements.EmptyObject"/> when the team has none.
    /// </summary>
    public required JsonElement Settings { get; init; }

    /// <summary>
    /// Gets the team's root keys, keyed by region, in a map that is empty when the team brings none.
    /// </summary>
    public required ImmutableDictionary<string, TeamRootKeyReference> RootKeys { get; init; }

    /// <summary>
    /// Gets the sequence number of the directory change that published this document, or <c>0</c> for a document that
    /// is not yet published.
    /// </summary>
    public long Sequence { get; init; }
}
