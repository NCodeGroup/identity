// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

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
    /// Gets the team's settings as a JSON object.
    /// </summary>
    public string SettingsJson { get; init; } = "{}";

    /// <summary>
    /// Gets the references to the team's root keys, keyed by region.
    /// </summary>
    public ImmutableDictionary<string, string> RootKeyReferences { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the sequence number of the directory change that published this document, or <c>0</c> for a document that
    /// is not yet published.
    /// </summary>
    public long Sequence { get; init; }
}
