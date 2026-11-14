// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Represents an ASP.NET Core Identity user that belongs to one tenant. The ASP.NET Core Identity user type of the
/// host implements it, so the adapter can stamp the owning tenant on a user it creates and treat a user of another
/// tenant as not found.
/// </summary>
[PublicAPI]
public interface ITenantBoundUser
{
    /// <summary>
    /// Gets or sets the identifier of the tenant that owns the user.
    /// </summary>
    string TenantId { get; set; }
}
