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
    /// Gets the shortest <see cref="TokenLifetime"/> the directory accepts.
    /// </summary>
    public static TimeSpan MinTokenLifetime => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets the longest <see cref="TokenLifetime"/> the directory accepts.
    /// </summary>
    public static TimeSpan MaxTokenLifetime => TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the issuer identifier of the directory, its public URL, which is the <c>iss</c> of the access
    /// tokens it issues to cells. It is required.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the key ring, of the top-level <c>KeyRings</c> section, whose keys sign the directory's
    /// access tokens. It is required.
    /// </summary>
    public string KeyRing { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long an access token that the directory issues is valid. It defaults to five minutes, and it is
    /// at least <see cref="MinTokenLifetime"/> and at most <see cref="MaxTokenLifetime"/>.
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the platform's trust anchor, the control-plane cell and its root tenant. It is required.
    /// </summary>
    public DirectoryControlPlaneOptions ControlPlane { get; set; } = new();
}
