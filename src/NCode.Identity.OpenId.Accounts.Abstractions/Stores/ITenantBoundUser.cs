// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Represents a user of a local-account store that keeps the users of every tenant together, such as the ASP.NET Core
/// Identity adapter. The host's user type implements it, so the store can stamp the owning tenant on a user it creates
/// and treat a user of another tenant as not found.
/// </summary>
[PublicAPI]
public interface ITenantBoundUser
{
    /// <summary>
    /// Gets or sets the identifier of the tenant that owns the user.
    /// </summary>
    string TenantId { get; set; }
}
