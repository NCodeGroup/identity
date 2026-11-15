// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// The PostgreSQL session settings that carry the ambient tenant scope to the row-level security policies.
/// </summary>
internal static class TenantSessionSettings
{
    /// <summary>
    /// The session setting that holds the ambient tenant identifier.
    /// </summary>
    public const string TenantId = "app.tenant_id";

    /// <summary>
    /// The session setting that holds whether the ambient scope is unscoped.
    /// </summary>
    public const string Unscoped = "app.unscoped";

    /// <summary>
    /// The value of <see cref="Unscoped"/> for an unscoped session.
    /// </summary>
    public const string On = "on";

    /// <summary>
    /// The value of <see cref="Unscoped"/> for a session that is not unscoped.
    /// </summary>
    public const string Off = "off";
}
