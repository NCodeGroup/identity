// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the root key that a team brings in one region: the key wrapper, the key it names and the identity that
/// reaches it. The cell in the region mirrors it into the team's owner root key.
/// </summary>
/// <param name="KeyWrapperName">The name of the key wrapper.</param>
/// <param name="KeyReference">The reference to the key.</param>
// See ADR-1411.
[PublicAPI]
public sealed record TeamRootKeyReference(string KeyWrapperName, string KeyReference)
{
    /// <summary>
    /// Gets the identity that the team grants and the cell takes on to reach the key, or <see langword="null"/> when
    /// the cell reaches the key as itself.
    /// </summary>
    public string? AccessIdentity { get; init; }
}
