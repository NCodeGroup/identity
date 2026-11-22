// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// Contains the options for keeping audit records in the audit store, where an operator queries them after the logs that
/// also carry them have rotated.
/// </summary>
// See ADR-1303.
public sealed class AuditRecordOptions
{
    /// <summary>
    /// Gets the name of the subsection of the OpenID configuration section that binds these options.
    /// </summary>
    public const string SectionName = "AuditRecords";

    /// <summary>
    /// Gets or sets a value indicating whether audit records are kept in the audit store. The default is
    /// <see langword="true"/>. Audit records are logged whatever this says.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how often each replica writes the audit records it holds to the store. A replica that stops abruptly
    /// loses at most this much of its records from the store, never from the log. The default is five seconds. It is at
    /// least one millisecond and at most about 49.7 days, the periods a timer accepts.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how many audit records a replica holds for the store before it drops new ones, which it counts and
    /// logs. The default is 10,000. It is at least one.
    /// </summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets how long the store keeps an audit record. The default is 90 days, the event history of CloudTrail. It
    /// is at least one day.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Gets or sets how often each replica removes the audit records older than <see cref="Retention"/>. The default is
    /// one hour. It is at least one millisecond and at most about 49.7 days, the periods a timer accepts.
    /// </summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets how often each replica looks for audit streams that are due. The default is five seconds. It is at
    /// least one second and at most five minutes.
    /// </summary>
    // See ADR-1534.
    public TimeSpan StreamPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long a stored audit record waits before a stream reads it, because replicas commit their
    /// batches independently and a record stored late must not be passed over. The default is one minute. It is at
    /// least one second and at most one hour.
    /// </summary>
    // See ADR-1534.
    public TimeSpan StreamSettleDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the greatest number of audit records a stream posts in one request. The default is 100. It is at
    /// least one and at most 1,000.
    /// </summary>
    // See ADR-1534.
    public int StreamBatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets how long a replica holds the lease of a stream it delivers, so that no other replica delivers the
    /// stream meanwhile. The default is two minutes. It is at least 30 seconds and at most one hour.
    /// </summary>
    // See ADR-1534.
    public TimeSpan StreamLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the delay before a stream attempts again after its first failed delivery; each further failure in a
    /// row doubles it, up to <see cref="StreamMaxRetryDelay"/>. The default is ten seconds. It is at least one second
    /// and at most one hour.
    /// </summary>
    // See ADR-1534.
    public TimeSpan StreamRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the longest delay before a stream attempts again after failed deliveries. The default is one hour.
    /// It is at least <see cref="StreamRetryDelay"/> and at most one day.
    /// </summary>
    // See ADR-1534.
    public TimeSpan StreamMaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the greatest number of due streams a replica delivers in one poll. The default is 16. It is at
    /// least one and at most 1,000.
    /// </summary>
    // See ADR-1534.
    public int StreamsPerPoll { get; set; } = 16;
}
