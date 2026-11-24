// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// The <c>local</c> connection strategy: a connection whose end users are the accounts of a named local-account store.
/// </summary>
// See ADR-1535 and ADR-1051.
internal sealed class LocalConnectionStrategy(LocalAccountStoreCatalog catalog)
    : IConnectionStrategy
{
    private const string StoreProperty = "store";

    /// <summary>
    /// Reads the store name from a connection's options.
    /// </summary>
    /// <param name="options">The options of a local connection.</param>
    /// <returns>The store name, or <see langword="null"/> when the options name none.</returns>
    private static string? ReadStore(JsonElement options) =>
        options.ValueKind == JsonValueKind.Object
        && options.TryGetProperty(StoreProperty, out var store)
        && store.ValueKind == JsonValueKind.String
            ? store.GetString()
            : null;

    /// <inheritdoc/>
    public string Name => ConnectionStrategyNames.Local;

    /// <inheritdoc/>
    public string SignInMethod => ConnectionSignInMethods.Password;

    /// <inheritdoc/>
    public string? ValidateOptions(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            return "The options of a local connection must be a JSON object.";
        }

        if (
            !options.TryGetProperty(StoreProperty, out var store)
            || store.ValueKind != JsonValueKind.String
        )
        {
            return $"The options of a local connection must have a string '{StoreProperty}' that names a local-account store.";
        }

        var storeName = store.GetString();
        if (storeName is null || !catalog.Contains(storeName))
        {
            return $"The local-account store '{storeName}' is not registered.";
        }

        return null;
    }

    /// <inheritdoc/>
    public string? ValidateOptionsChange(JsonElement current, JsonElement proposed)
    {
        // The store holds the connection's accounts, so a different store would leave them unreachable.
        var currentStore = ReadStore(current);
        var proposedStore = ReadStore(proposed);
        return string.Equals(currentStore, proposedStore, StringComparison.Ordinal)
            ? null
            : "The store of a local connection cannot change.";
    }
}
