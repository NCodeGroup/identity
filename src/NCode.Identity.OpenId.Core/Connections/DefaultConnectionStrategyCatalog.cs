// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Provides the default implementation of <see cref="IConnectionStrategyCatalog"/> over the registered
/// <see cref="IConnectionStrategy"/> strategy collection. Strategies are keyed by their
/// <see cref="IConnectionStrategy.Name"/>.
/// </summary>
/// <remarks>
/// The registered set is validated when the catalog is composed: a strategy with a null, empty or whitespace name, or a
/// name registered more than once, is an error, never ignored.
/// </remarks>
// See ADR-1535.
internal sealed class DefaultConnectionStrategyCatalog(IEnumerable<IConnectionStrategy> strategies)
    : IConnectionStrategyCatalog
{
    /// <summary>
    /// Indexes the strategies by their names.
    /// </summary>
    /// <param name="strategies">The registered strategies.</param>
    /// <returns>The strategies keyed by name.</returns>
    /// <exception cref="InvalidOperationException">A strategy has no name, or two strategies share a name.</exception>
    private static FrozenDictionary<string, IConnectionStrategy> Index(
        IEnumerable<IConnectionStrategy> strategies
    )
    {
        var strategiesByName = new Dictionary<string, IConnectionStrategy>(StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            var name = strategy.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                var strategyType = strategy.GetType().FullName;
                throw new InvalidOperationException(
                    $"The connection strategy '{strategyType}' has a null, empty or whitespace name."
                );
            }

            if (!strategiesByName.TryAdd(name, strategy))
            {
                throw new InvalidOperationException(
                    $"The connection strategy '{name}' is registered more than once."
                );
            }
        }

        return strategiesByName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private FrozenDictionary<string, IConnectionStrategy> StrategiesByName { get; } =
        Index(strategies);

    /// <inheritdoc />
    public IReadOnlyCollection<IConnectionStrategy> Strategies => StrategiesByName.Values;

    /// <inheritdoc />
    public bool TryGetStrategy(string name, [MaybeNullWhen(false)] out IConnectionStrategy strategy)
    {
        if (string.IsNullOrEmpty(name))
        {
            strategy = null;
            return false;
        }

        return StrategiesByName.TryGetValue(name, out strategy);
    }
}
