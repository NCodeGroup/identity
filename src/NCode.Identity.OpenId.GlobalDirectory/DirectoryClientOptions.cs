// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Configures how a cell reaches the global directory service. Bound from the <c>Directory</c> configuration section.
/// The cell authenticates as a confidential client of the control-plane cell, whose issuer is
/// <see cref="ControlPlaneOptions.Issuer"/>, with a <c>private_key_jwt</c> assertion signed by the named key ring.
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
    /// Gets or sets the identifier of the machine client that the cell authenticates as:
    /// <see cref="GlobalDirectoryProtocol.CellClientIdPrefix"/> followed by the cell's name.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the name of the key ring whose private key signs the client assertion.
    /// </summary>
    public string? KeyRing { get; set; }

    /// <summary>
    /// Gets or sets how often a cell that is not the control plane polls the directory's change feed to mirror the team
    /// and platform documents. It is the period of the change feed's timer, from 1 millisecond to about 49.7 days. The
    /// default is 30 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a cell waits for a token request to the control plane, its discovery and token calls
    /// together, before the request ends as a <see cref="DirectoryUnavailableException"/>. It is the period of a timer,
    /// from 1 millisecond to about 49.7 days. The default is 30 seconds.
    /// </summary>
    public TimeSpan TokenRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a cell waits for one call to the directory, its response body included, before the call
    /// ends as a <see cref="DirectoryUnavailableException"/>. It is the period of a timer, from 1 millisecond to about
    /// 49.7 days. The default is 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a cell remembers a failed token request, during which a caller that needs that token fails
    /// at once without a request to the control plane. It is a timer period, from 1 millisecond to about 49.7 days. The
    /// default is 5 seconds.
    /// </summary>
    public TimeSpan TokenFailureCacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long the control-plane cell's publisher waits for a wake-up when its outbox is empty before it
    /// looks again. It is a timer period, from 1 millisecond to about 49.7 days. The default is 5 seconds.
    /// </summary>
    public TimeSpan PublishInterval { get; set; } = TimeSpan.FromSeconds(5);
}
