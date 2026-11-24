// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains the user name and email address that a local account is proposed to carry, for the checks that keep a
/// connection's identifiers unique and unambiguous.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class LocalAccountIdentifierProposal
{
    /// <summary>
    /// Gets the proposed user name, or <c>null</c> when the account has none.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Gets the proposed email address, or <c>null</c> when the account has none.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Gets the identifier of the account being updated, or <c>null</c> when the account is being created. An account
    /// with this identifier never conflicts with the proposal.
    /// </summary>
    public string? LocalAccountId { get; init; }
}
