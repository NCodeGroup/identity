// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// The declared shape of the <c>DirectoryDatabase</c> configuration section. The section binds strictly, so a key that
/// is not one of these properties stops the host.
/// </summary>
internal sealed class DirectoryDatabaseSettings
{
    /// <summary>
    /// The name of the Postgres role that the directory's replicas connect as.
    /// </summary>
    public string? RuntimeRole { get; set; }
}
