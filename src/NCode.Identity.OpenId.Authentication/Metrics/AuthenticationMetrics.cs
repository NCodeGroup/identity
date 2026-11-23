// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;

namespace NCode.Identity.OpenId.Authentication.Metrics;

/// <summary>
/// Owns the instruments of the <see cref="OpenIdAuthenticationMetrics.MeterName"/> meter, created once through the
/// <see cref="IMeterFactory"/> so each service provider has its own; call sites record through its methods and never
/// touch the meter directly.
/// </summary>
// See ADR-1197.
internal sealed class AuthenticationMetrics
{
    internal const string RateLimitingRequestsName = "ncode.identity.rate_limiting.requests";

    internal const string RateLimitingPartitionAttribute = "ncode.identity.rate_limiting.partition";

    internal const string RateLimitingResultAttribute = "ncode.identity.rate_limiting.result";

    internal const string TenantEnvironmentTypeAttribute = "ncode.identity.tenant.environment_type";

    internal const string AdmittedResult = "admitted";

    internal const string RejectedResult = "rejected";

    internal const string OverageResult = "overage";

    internal const string StoreUnavailableResult = "store_unavailable";

    internal const string OutboundRetriesName = "ncode.identity.outbound_http.retries";

    internal const string OutboundTimeoutsName = "ncode.identity.outbound_http.timeouts";

    internal const string ErrorTypeAttribute = "error.type";

    internal const string TimeoutScopeAttribute = "ncode.identity.outbound_http.timeout_scope";

    internal const string TimeoutErrorType = "timeout";

    internal const string AttemptTimeoutScope = "attempt";

    internal const string TotalTimeoutScope = "total";

    internal const string AdmissionShedName = "ncode.identity.admission.shed";

    internal const string AuditStreamDeliveriesName = "ncode.identity.audit_stream.deliveries";

    internal const string AuditStreamRecordsName = "ncode.identity.audit_stream.records";

    internal const string AuditStreamResultAttribute = "ncode.identity.audit_stream.result";

    internal const string DeliveredResult = "delivered";

    internal const string FailedResult = "failed";

    private Counter<long> RateLimitingRequests { get; }

    private Counter<long> AdmissionShed { get; }

    private Counter<long> AuditStreamDeliveries { get; }

    private Counter<long> AuditStreamRecords { get; }

    private Counter<long> OutboundRetries { get; }

    private Counter<long> OutboundTimeouts { get; }

    public AuthenticationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(OpenIdAuthenticationMetrics.MeterName);

        RateLimitingRequests = meter.CreateCounter<long>(
            RateLimitingRequestsName,
            unit: "{request}",
            description: "The number of requests judged against a rate-limit partition, by partition and result."
        );

        AdmissionShed = meter.CreateCounter<long>(
            AdmissionShedName,
            unit: "{request}",
            description: "The number of protocol requests a replica shed because its in-flight requests filled the share of its capacity their tenant may fill, by the tenant's environment type."
        );

        AuditStreamDeliveries = meter.CreateCounter<long>(
            AuditStreamDeliveriesName,
            unit: "{request}",
            description: "The number of batches of audit records the server posted to audit streams, by result."
        );

        AuditStreamRecords = meter.CreateCounter<long>(
            AuditStreamRecordsName,
            unit: "{record}",
            description: "The number of audit records audit streams accepted."
        );

        OutboundRetries = meter.CreateCounter<long>(
            OutboundRetriesName,
            unit: "{retry}",
            description: "The number of times a call to a caller-supplied URL was sent again after a transient failure, by the failure."
        );

        OutboundTimeouts = meter.CreateCounter<long>(
            OutboundTimeoutsName,
            unit: "{timeout}",
            description: "The number of times an attempt or a whole call to a caller-supplied URL timed out, by scope."
        );
    }

    /// <summary>
    /// Records that the guarded outbound client sends a call again after a transient failure.
    /// </summary>
    /// <param name="errorType">The bounded failure: the response's status code, <c>timeout</c>, or the
    /// <see cref="HttpRequestError"/> in snake case.</param>
    public void OutboundRetried(string errorType)
    {
        var errorTypeTag = new KeyValuePair<string, object?>(ErrorTypeAttribute, errorType);
        OutboundRetries.Add(1, errorTypeTag);
    }

    /// <summary>
    /// Records that an attempt, or a whole call, of the guarded outbound client timed out.
    /// </summary>
    /// <param name="scope">The bounded scope: <c>attempt</c> or <c>total</c>.</param>
    public void OutboundTimedOut(string scope)
    {
        var scopeTag = new KeyValuePair<string, object?>(TimeoutScopeAttribute, scope);
        OutboundTimeouts.Add(1, scopeTag);
    }

    /// <summary>
    /// Records that a request was judged against a registered rate-limit descriptor.
    /// </summary>
    /// <param name="partition">The name of the registered rate-limit descriptor.</param>
    /// <param name="result">The bounded result: <c>admitted</c>, <c>overage</c>, <c>rejected</c>, or
    /// <c>store_unavailable</c>.</param>
    /// <param name="environmentType">The tenant's registered environment type, a bounded value.</param>
    public void RateLimitJudged(string partition, string result, string environmentType)
    {
        var partitionTag = new KeyValuePair<string, object?>(
            RateLimitingPartitionAttribute,
            partition
        );
        var resultTag = new KeyValuePair<string, object?>(RateLimitingResultAttribute, result);
        var environmentTypeTag = new KeyValuePair<string, object?>(
            TenantEnvironmentTypeAttribute,
            environmentType
        );
        RateLimitingRequests.Add(1, partitionTag, resultTag, environmentTypeTag);
    }

    /// <summary>
    /// Records that a replica shed a protocol request because its in-flight requests filled the share of its capacity
    /// the request's tenant may fill.
    /// </summary>
    /// <param name="environmentType">The tenant's registered environment type, a bounded value.</param>
    public void RequestShed(string environmentType)
    {
        var environmentTypeTag = new KeyValuePair<string, object?>(
            TenantEnvironmentTypeAttribute,
            environmentType
        );
        AdmissionShed.Add(1, environmentTypeTag);
    }

    /// <summary>
    /// Records that an audit stream's endpoint accepted a batch of audit records.
    /// </summary>
    /// <param name="records">The number of audit records in the batch.</param>
    // See ADR-1534.
    public void AuditStreamDelivered(int records)
    {
        var resultTag = new KeyValuePair<string, object?>(
            AuditStreamResultAttribute,
            DeliveredResult
        );
        AuditStreamDeliveries.Add(1, resultTag);
        AuditStreamRecords.Add(records);
    }

    /// <summary>
    /// Records that a batch of audit records could not be delivered to an audit stream's endpoint.
    /// </summary>
    // See ADR-1534.
    public void AuditStreamFailed()
    {
        var resultTag = new KeyValuePair<string, object?>(AuditStreamResultAttribute, FailedResult);
        AuditStreamDeliveries.Add(1, resultTag);
    }
}
