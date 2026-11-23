// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// The <c>local</c> connection strategy: a connection whose end users are the accounts of a named local-account store.
/// </summary>
// See ADR-1535 and ADR-1051.
[PublicAPI]
public sealed class LocalConnectionStrategy(LocalAccountStoreCatalog catalog) : IConnectionStrategy
{
    private const string StoreProperty = "store";

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
}
