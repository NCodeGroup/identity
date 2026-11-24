// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains the identifier an end user presents to sign in to a local connection: a user name, an email address, or
/// both. A store runs the lookup of each value the identifier carries, whatever the other finds, so the work does not
/// tell which one matched, and the match by user name wins.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
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
}
