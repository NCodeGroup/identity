// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Reports the outcome of importing the master keys of an owner from a transfer package: the master keys stored, or the
/// reason that the root key the package brings is refused, in which case nothing is stored.
/// </summary>
/// <param name="Count">The number of master keys that were stored, which is zero when the import is refused.</param>
// See ADR-1514.
[PublicAPI]
public sealed record MasterKeyTransferResult(int Count)
{
    /// <summary>
    /// Gets the reason that the root key the package brings is refused, or <see langword="null"/> when the master keys
    /// are stored.
    /// </summary>
    public string? RefusalDetail { get; init; }

    /// <summary>
    /// Gets a value indicating whether the master keys were stored, which is when <see cref="RefusalDetail"/> is
    /// <see langword="null"/>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(RefusalDetail))]
    public bool IsImported => RefusalDetail is null;
}
