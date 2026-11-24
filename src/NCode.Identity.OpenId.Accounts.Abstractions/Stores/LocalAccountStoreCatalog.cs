// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Indexes the registered local-account stores by name and resolves the store that a local connection's options select.
/// </summary>
[PublicAPI]
public sealed class LocalAccountStoreCatalog
{
    private Dictionary<string, LocalAccountStoreRegistration> Registrations { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalAccountStoreCatalog"/> class.
    /// </summary>
    /// <param name="registrations">The registered local-account stores.</param>
    /// <exception cref="InvalidOperationException">A registration has a null or empty name, or two registrations have
    /// the same name.</exception>
    public LocalAccountStoreCatalog(IEnumerable<LocalAccountStoreRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        Registrations = new Dictionary<string, LocalAccountStoreRegistration>(
            StringComparer.Ordinal
        );
        foreach (var registration in registrations)
        {
            if (string.IsNullOrEmpty(registration.Name))
            {
                throw new InvalidOperationException(
                    "A local-account store is registered with a null or empty name."
                );
            }

            if (!Registrations.TryAdd(registration.Name, registration))
            {
                throw new InvalidOperationException(
                    $"More than one local-account store is registered under the name '{registration.Name}'."
                );
            }
        }
    }

    /// <summary>
    /// Determines whether a local-account store is registered under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the store.</param>
    /// <returns><c>true</c> if a store is registered under the name; otherwise, <c>false</c>.</returns>
    public bool Contains(string name)
    {
        return Registrations.ContainsKey(name);
    }

    /// <summary>
    /// Gets the <see cref="ILocalAccountStore"/> that holds the accounts of a local connection.
    /// </summary>
    /// <param name="storeManager">The unit of work that the store works in.</param>
    /// <param name="connection">The local connection.</param>
    /// <returns>The store of the connection.</returns>
    /// <exception cref="InvalidOperationException">The connection is not a local connection, or its store is not
    /// registered.</exception>
    public ILocalAccountStore GetStore(IStoreManager storeManager, PersistedConnection connection)
    {
        var registration = GetRegistration(connection);
        return registration.CreateStore(storeManager);
    }

    /// <summary>
    /// Gets the <see cref="ILocalAccountTransferStore"/> that holds the accounts of a local connection.
    /// </summary>
    /// <param name="storeManager">The unit of work that the store works in.</param>
    /// <param name="connection">The local connection.</param>
    /// <returns>The transfer store of the connection.</returns>
    /// <exception cref="InvalidOperationException">The connection is not a local connection, or its store is not
    /// registered.</exception>
    public ILocalAccountTransferStore GetTransferStore(
        IStoreManager storeManager,
        PersistedConnection connection
    )
    {
        var registration = GetRegistration(connection);
        return registration.CreateTransferStore(storeManager);
    }

    private LocalAccountStoreRegistration GetRegistration(PersistedConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!ConnectionStrategyNames.IsLocal(connection.Strategy))
        {
            throw new InvalidOperationException(
                $"The connection '{connection.Name}' has the strategy '{connection.Strategy}', not '{ConnectionStrategyNames.Local}', so it has no local-account store."
            );
        }

        var options = connection.Options.Deserialize<LocalConnectionOptions>();
        if (options is null)
        {
            throw new InvalidOperationException(
                $"The options of the connection '{connection.Name}' are not a local connection's options."
            );
        }

        if (!Registrations.TryGetValue(options.Store, out var registration))
        {
            throw new InvalidOperationException(
                $"The connection '{connection.Name}' names the local-account store '{options.Store}', which is not registered."
            );
        }

        return registration;
    }
}
