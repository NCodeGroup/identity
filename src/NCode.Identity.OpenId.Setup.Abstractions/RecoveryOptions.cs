// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Configures the headless recovery commands. Bound from the <c>Recovery</c> configuration section.
/// </summary>
[PublicAPI]
public sealed class RecoveryOptions
{
    /// <summary>
    /// The name of the configuration section the options bind from.
    /// </summary>
    public const string SectionName = "Recovery";

    /// <summary>
    /// Gets or sets the administrator that the <c>recover-admin</c> command creates or restores.
    /// </summary>
    public RecoveryAdminOptions Admin { get; set; } = new();
}
