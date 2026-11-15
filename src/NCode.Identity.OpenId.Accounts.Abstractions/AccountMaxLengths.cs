// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts;

/// <summary>
/// Contains constants for the maximum lengths of local account fields.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public static class AccountMaxLengths
{
    /// <summary>
    /// Specifies the length of a tenant identifier of up to 63 characters and the <c>|</c> separator that a store
    /// puts before a normalized user name or email address.
    /// </summary>
    private const int TenantLookupPrefixLength = 64;

    /// <summary>
    /// Specifies the maximum length of an account's <c>UserName</c> (login handle).
    /// </summary>
    public const int UserName = 300;

    /// <summary>
    /// Specifies the maximum length of an account's normalized user name in a store that puts the tenant identifier,
    /// up to 63 characters, and a <c>|</c> separator before it, such as the ASP.NET Core Identity adapter, for a user
    /// name whose normalized form holds at most <see cref="UserName"/> characters. A store that keeps the tenant in
    /// another column holds at most <see cref="UserName"/>.
    /// </summary>
    public const int NormalizedUserName = UserName + TenantLookupPrefixLength;

    /// <summary>
    /// Specifies the maximum length of an account's <c>Email</c> address.
    /// </summary>
    public const int Email = 300;

    /// <summary>
    /// Specifies the maximum length of an account's normalized email address in a store that puts the tenant
    /// identifier, up to 63 characters, and a <c>|</c> separator before it, such as the ASP.NET Core Identity
    /// adapter, for an email address whose normalized form holds at most <see cref="Email"/> characters. A store that
    /// keeps the tenant in another column holds at most <see cref="Email"/>.
    /// </summary>
    public const int NormalizedEmail = Email + TenantLookupPrefixLength;

    /// <summary>
    /// Specifies the maximum length of an account's hashed password.
    /// </summary>
    public const int PasswordHash = 1000;

    /// <summary>
    /// Specifies the maximum length of an account's <c>SecurityStamp</c>.
    /// </summary>
    public const int SecurityStamp = 100;

    /// <summary>
    /// Specifies the maximum length of a profile claim's <c>Type</c>.
    /// </summary>
    public const int ClaimType = 300;

    /// <summary>
    /// Specifies the maximum length of a profile claim's <c>Value</c>.
    /// </summary>
    public const int ClaimValue = 2000;
}
