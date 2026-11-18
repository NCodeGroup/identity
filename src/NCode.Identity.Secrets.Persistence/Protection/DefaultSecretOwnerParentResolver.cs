// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Resolves the platform owner as the only ancestor of every owner except the platform owner, which has none.
/// </summary>
// See ADR-1414.
internal sealed class DefaultSecretOwnerParentResolver : ISecretOwnerParentResolver
{
    private static IReadOnlyList<SecretOwner> Empty { get; } = [];

    private static IReadOnlyList<SecretOwner> PlatformOnly { get; } = [SecretOwner.Platform];

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SecretOwner>> GetAncestorsAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    )
    {
        var isPlatform = string.Equals(
            owner.OwnerType,
            SecretOwnerTypes.Platform,
            StringComparison.OrdinalIgnoreCase
        );

        return ValueTask.FromResult(isPlatform ? Empty : PlatformOnly);
    }
}
