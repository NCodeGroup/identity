// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Resolves the ancestors of a secret owner, which are the owners whose root keys can wrap the owner's master keys when
/// the owner brings none of its own.
/// </summary>
/// <remarks>
/// The ancestors of an owner run from the nearest to the platform owner, which is always the last. The platform owner
/// has no ancestors.
/// </remarks>
// See ADR-1414.
[PublicAPI]
public interface ISecretOwnerParentResolver
{
    /// <summary>
    /// Gets the ancestors of an owner, nearest first, ending with the platform owner.
    /// </summary>
    /// <param name="owner">The owner whose ancestors to get, in its normalized form.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The ancestors of <paramref name="owner"/>, nearest first and ending with <see cref="SecretOwner.Platform"/>; an
    /// empty list when <paramref name="owner"/> is the platform owner.
    /// </returns>
    ValueTask<IReadOnlyList<SecretOwner>> GetAncestorsAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    );
}
