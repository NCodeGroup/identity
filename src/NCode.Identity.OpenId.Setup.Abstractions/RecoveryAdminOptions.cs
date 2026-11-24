// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Describes the administrator that the headless <c>recover-admin</c> command creates or restores.
/// </summary>
[PublicAPI]
public sealed class RecoveryAdminOptions
{
    /// <summary>
    /// Gets or sets the administrator's username.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the administrator's display name, or <c>null</c> to use the username.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the administrator's email address, or <c>null</c> when none is recorded.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the administrator's new password.
    /// </summary>
    public string? Password { get; set; }
}
