// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Accounts.DataContracts;

// See ADR-1080.
/// <summary>
/// Contains a lightweight read projection of a local account's names (its identifier, username and display name)
/// without its claims, credential or status, for a reader that names the account's principal while it fetches only
/// those columns.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class LocalAccountNameProjection
{
    /// <summary>
    /// Gets the opaque, server-generated identifier of the local account (its self-issued subject).
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string LocalAccountId { get; init; }

    /// <summary>
    /// Gets the username (login handle) of the local account.
    /// </summary>
    [MaxLength(AccountMaxLengths.UserName)]
    public required string UserName { get; init; }

    /// <summary>
    /// Gets the display name of the local account.
    /// </summary>
    [MaxLength(AccountMaxLengths.DisplayName)]
    public required string DisplayName { get; init; }
}
