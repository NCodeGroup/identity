// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Configures how a cell reaches the global directory service. Bound from the <c>Directory</c> configuration section.
/// The cell authenticates to the directory's own token endpoint as a confidential client, with a <c>private_key_jwt</c>
/// assertion signed by the named key ring.
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
    /// Gets or sets the path of a PEM file of one or more certificate authority certificates. When it is set, the
    /// directory's server certificate must chain to one of them, and the process's system trust store is not used for
    /// calls to the directory; every other outbound call of the process keeps the system store. A directory served
    /// under a private certificate authority sets it. The file must exist and hold at least one certificate.
    /// </summary>
    public string? CaCertificatesPath { get; set; }

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
    /// Gets or sets how long one attempt of a call that may be retried — a GET or HEAD without a body, while
    /// <see cref="MaxRetryAttempts"/> is above zero — may take until its response headers arrive; a longer attempt is
    /// cancelled and retried. The default is ten seconds. It is between ten milliseconds and five minutes, and not
    /// greater than <see cref="TotalTimeout"/>.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long a whole call to the directory may take: every attempt, the delays between retries, and
    /// reading the response body. A call that outlasts it ends as a <see cref="DirectoryUnavailableException"/>. The
    /// default is 15 seconds. It is between ten milliseconds and five minutes.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets how many times a failed attempt is retried; zero turns retries off. Only a GET or HEAD request
    /// without a body is retried, after a connection failure, a timed-out attempt, or a 408, 429 or 5xx response. The
    /// default is two. It is between zero and ten.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Gets or sets the base delay before a retry; it grows exponentially and is jittered. The default is one second.
    /// It is between zero and five minutes.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how long a cell remembers a failed token request, during which a caller that needs that token fails
    /// at once without a request to the directory. It is a timer period, from 1 millisecond to about 49.7 days. The
    /// default is 5 seconds.
    /// </summary>
    public TimeSpan TokenFailureCacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long the control-plane cell's publisher waits for a wake-up when its outbox is empty before it
    /// looks again. It is a timer period, from 1 millisecond to about 49.7 days. The default is 5 seconds.
    /// </summary>
    public TimeSpan PublishInterval { get; set; } = TimeSpan.FromSeconds(5);
}
