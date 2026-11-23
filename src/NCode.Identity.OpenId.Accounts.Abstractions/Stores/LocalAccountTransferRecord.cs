// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains one local account as an <see cref="ILocalAccountTransferStore"/> reads and writes it as a whole: the
/// profile, the credential exactly as the store holds it, the security stamp, the lockout state and the profile claims.
/// Unlike <see cref="PersistedLocalAccount"/>, it carries the password hash, so it is sensitive and is handled only
/// inside an encrypted transfer.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class LocalAccountTransferRecord
{
    /// <summary>
    /// Gets the opaque public identifier of the local account, which an import keeps.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string LocalAccountId { get; init; }

    /// <summary>
    /// Gets the account's username (login handle), or <c>null</c> when it signs in by email address only.
    /// </summary>
    [MaxLength(AccountMaxLengths.UserName)]
    public required string? UserName { get; init; }

    /// <summary>
    /// Gets the account's display name, the end user's name as people read it; it is the source of the account's
    /// <c>name</c> claim and is never empty.
    /// </summary>
    [MaxLength(AccountMaxLengths.DisplayName)]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the normalized username as the exporting store holds it, or <c>null</c> when the account has none.
    /// </summary>
    [MaxLength(AccountMaxLengths.NormalizedUserName)]
    public required string? NormalizedUserName { get; init; }

    /// <summary>
    /// Gets the account's email address, or <c>null</c> when none is recorded.
    /// </summary>
    [MaxLength(AccountMaxLengths.Email)]
    public required string? Email { get; init; }

    /// <summary>
    /// Gets the normalized email address as the exporting store holds it, or <c>null</c> when none is recorded.
    /// </summary>
    [MaxLength(AccountMaxLengths.NormalizedEmail)]
    public required string? NormalizedEmail { get; init; }

    /// <summary>
    /// Gets a value indicating whether the account's <see cref="Email"/> is verified.
    /// </summary>
    public required bool EmailVerified { get; init; }

    /// <summary>
    /// Gets the password hash as the exporting store holds it, or <c>null</c> for a passwordless account. A hash
    /// verifies in an importing store that hashes passwords the way the exporting store does.
    /// </summary>
    [MaxLength(AccountMaxLengths.PasswordHash)]
    public required string? PasswordHash { get; init; }

    /// <summary>
    /// Gets the account's security stamp.
    /// </summary>
    [MaxLength(AccountMaxLengths.SecurityStamp)]
    public required string SecurityStamp { get; init; }

    /// <summary>
    /// Gets a value indicating whether the account is enabled, which is whether it may sign in.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether lockout applies to the account.
    /// </summary>
    public required bool LockoutEnabled { get; init; }

    /// <summary>
    /// Gets the end of the account's lockout, or <c>null</c> when it is not locked out.
    /// </summary>
    public required DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>
    /// Gets the number of consecutive failed sign-in attempts the store counts for the account.
    /// </summary>
    public required int AccessFailedCount { get; init; }

    /// <summary>
    /// Gets the account's profile claims.
    /// </summary>
    public required IReadOnlyList<PersistedLocalAccountClaim> Claims { get; init; }
}
