// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Represents a user of a local-account store that keeps the users of every tenant and connection together, such as the
/// ASP.NET Core Identity adapter. The host's user type implements it, so the store can stamp the owning tenant and
/// connection on a user it creates and treat a user of another tenant or connection as not found.
/// </summary>
[PublicAPI]
public interface ITenantBoundUser
{
    /// <summary>
    /// Gets or sets the identifier of the tenant that owns the user.
    /// </summary>
    string TenantId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the local connection that holds the user.
    /// </summary>
    string ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the account's display name, which the ASP.NET Core Identity store reads and writes.
    /// </summary>
    string DisplayName { get; set; }
}
