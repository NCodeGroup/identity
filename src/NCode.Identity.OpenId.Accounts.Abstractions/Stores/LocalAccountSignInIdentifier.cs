// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains the identifier an end user presents to sign in to a local connection: a user name, an email address, or
/// both. A store runs the lookup of each value the identifier carries, whatever the other finds, so the work does not
/// tell which one matched, and the match by user name wins.
/// </summary>
[PublicAPI]
public sealed class LocalAccountSignInIdentifier
{
    /// <summary>
    /// Gets the identifier of the local connection whose accounts are searched.
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets the user name to look up, or <c>null</c> when the identifier is not a user name.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Gets the email address to look up, or <c>null</c> when the identifier is not an email address.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Builds the identifier a store looks an account up by, from the value an end user submitted and the connection's
    /// <c>local_account_identifier</c> mode: an email address, a user name, or either.
    /// </summary>
    /// <param name="connectionId">The identifier of the local connection whose accounts are searched.</param>
    /// <param name="mode">The connection's <c>local_account_identifier</c> setting.</param>
    /// <param name="submitted">The value the end user submitted.</param>
    /// <returns>The identifier that carries each value the mode looks the submitted value up as.</returns>
    public static LocalAccountSignInIdentifier ForSubmitted(
        string connectionId,
        string mode,
        string submitted
    ) =>
        mode switch
        {
            LocalAccountIdentifiers.Email => new LocalAccountSignInIdentifier
            {
                ConnectionId = connectionId,
                Email = submitted,
            },
            LocalAccountIdentifiers.UserName => new LocalAccountSignInIdentifier
            {
                ConnectionId = connectionId,
                UserName = submitted,
            },
            _ => new LocalAccountSignInIdentifier
            {
                ConnectionId = connectionId,
                UserName = submitted,
                Email = submitted,
            },
        };
}
