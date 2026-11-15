// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Configures the global directory server. Bound from the <c>DirectoryServer</c> configuration section.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class DirectoryServerOptions
{
    /// <summary>
    /// The name of the configuration section that binds <see cref="DirectoryServerOptions"/>.
    /// </summary>
    public const string SectionName = "DirectoryServer";

    /// <summary>
    /// Gets or sets the issuer identifier of the control-plane cell whose access tokens the directory accepts. The
    /// directory validates a bearer token against this issuer's discovery document and signing keys. It is required.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the name of the control-plane cell. Only that cell publishes the platform document, reserves,
    /// publishes, releases or retires a team, and registers itself as the control plane; it also releases, retires and
    /// changes the issuer of any tenant. It is required.
    /// </summary>
    public string? ControlPlaneCell { get; set; }
}
