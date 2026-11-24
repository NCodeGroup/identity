// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains the registration of one named local-account store: the name that a local connection's options select, and
/// the factories of the store and its transfer store.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class LocalAccountStoreRegistration
{
    /// <summary>
    /// Gets the name of the store, which a local connection's <c>store</c> option selects. Names are compared
    /// ordinally.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the factory of the <see cref="ILocalAccountStore"/> over a unit of work.
    /// </summary>
    public required Func<IStoreManager, ILocalAccountStore> CreateStore { get; init; }

    /// <summary>
    /// Gets the factory of the <see cref="ILocalAccountTransferStore"/> over a unit of work.
    /// </summary>
    public required Func<
        IStoreManager,
        ILocalAccountTransferStore
    > CreateTransferStore { get; init; }
}
