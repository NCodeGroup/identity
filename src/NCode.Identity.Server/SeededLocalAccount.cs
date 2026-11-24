// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.Server;

/// <summary>
/// Describes a local account that a configuration seeder is about to create, with the tenant, connection and store it
/// is seeded into.
/// </summary>
internal sealed class SeededLocalAccount
{
    /// <summary>
    /// Gets the kind of account the seeder creates, which names the account in an error.
    /// </summary>
    public required string AccountKind { get; init; }

    /// <summary>
    /// Gets the tenant the account is seeded into.
    /// </summary>
    public required PersistedTenant Tenant { get; init; }

    /// <summary>
    /// Gets the local connection the account is seeded into.
    /// </summary>
    public required PersistedConnection Connection { get; init; }

    /// <summary>
    /// Gets the <see cref="ILocalAccountStore"/> of the connection.
    /// </summary>
    public required ILocalAccountStore Store { get; init; }

    /// <summary>
    /// Gets the local account the seeder is about to create.
    /// </summary>
    public required PersistedLocalAccount Account { get; init; }
}
