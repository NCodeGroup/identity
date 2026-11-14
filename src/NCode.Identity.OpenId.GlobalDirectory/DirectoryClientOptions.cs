// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Configures how a cell reaches the global directory service. Bound from the <c>Directory</c> configuration section.
/// The cell authenticates as a confidential client of the control-plane cell, whose issuer is
/// <see cref="CellOptions.ControlPlane"/>, with a <c>private_key_jwt</c> assertion signed by the named key ring.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class DirectoryClientOptions
{
    /// <summary>
    /// The name of the configuration section that binds <see cref="DirectoryClientOptions"/>.
    /// </summary>
    public const string SectionName = "Directory";

    /// <summary>
    /// Gets or sets the base URL of the directory service. The remote adapters are registered only when it is set. It
    /// is <c>https</c>, or <c>http</c> on a loopback host.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the machine client that the cell authenticates as, <c>cell-{name}</c>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the name of the key ring whose private key signs the client assertion.
    /// </summary>
    public string? KeyRing { get; set; }

    /// <summary>
    /// Gets or sets how often a cell that is not the control plane polls the directory's change feed to mirror the
    /// team and platform documents. It must be positive. The default is 30 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
