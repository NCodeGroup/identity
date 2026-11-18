// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// The declared shape of the <c>HostDatabase</c> configuration section. The section binds strictly, so a key that is
/// not one of these properties stops the host.
/// </summary>
[PublicAPI]
public sealed class HostDatabaseSettings
{
    /// <summary>
    /// The name of the Postgres role that the host runs as.
    /// </summary>
    public string? RuntimeRole { get; set; }
}
