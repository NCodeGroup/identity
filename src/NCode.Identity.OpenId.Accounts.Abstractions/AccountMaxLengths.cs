// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Accounts;

/// <summary>
/// Contains constants for the maximum lengths of local account fields.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public static class AccountMaxLengths
{
    /// <summary>
    /// Specifies the length of the <c>{tenant}|{connection}|</c> prefix a store puts before a normalized user name or
    /// email address: a tenant identifier of up to <see cref="TenantIdentifierRules.MaxTenantIdLength"/> characters, a
    /// <c>|</c> separator, a connection identifier of up to <see cref="MaxLengths.ResourceId"/> characters and a
    /// <c>|</c> separator.
    /// </summary>
    private const int TenantLookupPrefixLength =
        TenantIdentifierRules.MaxTenantIdLength + 1 + MaxLengths.ResourceId + 1;

    /// <summary>
    /// Specifies the maximum length of an account's <c>UserName</c> (login handle).
    /// </summary>
    public const int UserName = 300;

    /// <summary>
    /// Specifies the maximum length of an account's normalized user name in a store that puts the
    /// <c>{tenant}|{connection}|</c> prefix before it, such as the ASP.NET Core Identity adapter, for a user name whose
    /// normalized form holds at most <see cref="UserName"/> characters. A store that keeps the tenant and connection in
    /// other columns holds at most <see cref="UserName"/>.
    /// </summary>
    public const int NormalizedUserName = UserName + TenantLookupPrefixLength;

    /// <summary>
    /// Specifies the maximum length of an account's display name.
    /// </summary>
    public const int DisplayName = 300;

    /// <summary>
    /// Specifies the maximum length of an account's <c>Email</c> address.
    /// </summary>
    public const int Email = 300;

    /// <summary>
    /// Specifies the maximum length of an account's normalized email address in a store that puts the
    /// <c>{tenant}|{connection}|</c> prefix before it, such as the ASP.NET Core Identity adapter, for an email address
    /// whose normalized form holds at most <see cref="Email"/> characters. A store that keeps the tenant and connection
    /// in other columns holds at most <see cref="Email"/>.
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
