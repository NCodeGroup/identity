// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// The registry of the <see cref="IConnectionStrategy"/> implementations known to the OP.
/// </summary>
// See ADR-1535.
[PublicAPI]
public interface IConnectionStrategyCatalog
{
    /// <summary>
    /// Gets every registered strategy.
    /// </summary>
    IReadOnlyCollection<IConnectionStrategy> Strategies { get; }

    /// <summary>
    /// Attempts to get the registered strategy named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The strategy name to look up (for example <c>local</c>).</param>
    /// <param name="strategy">When this method returns, contains the matching strategy, if one is registered.</param>
    /// <returns><see langword="true"/> if a strategy is registered for the name; otherwise, <see langword="false"/>.</returns>
    bool TryGetStrategy(string name, [MaybeNullWhen(false)] out IConnectionStrategy strategy);
}
