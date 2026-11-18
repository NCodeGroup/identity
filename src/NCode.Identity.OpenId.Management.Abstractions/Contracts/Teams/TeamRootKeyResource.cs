// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Teams;

/// <summary>
/// Represents the REST resource for the root key that a team brings in one region.
/// </summary>
// See ADR-1411 and ADR-1414.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TeamRootKeyResource
{
    /// <summary>
    /// Gets the region whose cell wraps the team's master keys under the key.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets the name of the key wrapper that wraps under the key.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the reference to the key, such as the ARN of a KMS key.
    /// </summary>
    public required string KeyReference { get; init; }

    /// <summary>
    /// Gets the identity that the cell takes on to reach the key, or <see langword="null"/> when the cell reaches the
    /// key with the deployment's own identity.
    /// </summary>
    public string? AccessIdentity { get; init; }
}
