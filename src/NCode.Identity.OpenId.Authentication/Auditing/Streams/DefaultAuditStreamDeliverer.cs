// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.OpenId.Authentication.OutboundHttp;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Provides the default implementation of <see cref="IAuditStreamDeliverer"/>. In a scope of the stream's tenant it
/// reads the audit records stored after the stream's position, posts each batch to the stream's endpoint signed with
/// every current signing secret, and moves the position after the endpoint accepts the batch, so a record is delivered
/// at least once. A failed delivery keeps the position and backs the stream off. Every write of the stream's state
/// reads the state again first and stops when the replica no longer holds the stream's lease, because each save gives
/// the stored state a fresh concurrency token.
/// </summary>
// See ADR-1534 and ADR-1012.
internal sealed class DefaultAuditStreamDeliverer(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ISecretProtectorProvider secretProtectorProvider,
    IHttpClientFactory httpClientFactory,
    IOptions<AuditRecordOptions> options,
    AuthenticationMetrics metrics,
    TimeProvider timeProvider,
    ILogger<DefaultAuditStreamDeliverer> logger
) : IAuditStreamDeliverer
{
    /// <summary>
    /// The error of a delivery that has no current signing secret to sign with.
    /// </summary>
    internal const string NoSigningSecretError = "no_signing_secret";

    /// <summary>
    /// The error of a delivery whose request failed before the endpoint answered.
    /// </summary>
    internal const string RequestFailedError = "request_failed";

    /// <summary>
    /// The error of a delivery that did not complete in time.
    /// </summary>
    internal const string TimeoutError = "timeout";

    /// <summary>
    /// The error of a delivery whose signing secrets could not be unprotected.
    /// </summary>
    internal const string SigningSecretUnavailableError = "signing_secret_unavailable";

    /// <summary>
    /// The error of a delivery that failed for a reason other than its endpoint.
    /// </summary>
    internal const string DeliveryError = "delivery_error";

    private const string WebhookIdHeader = "webhook-id";

    private const string WebhookTimestampHeader = "webhook-timestamp";

    private const string WebhookSignatureHeader = "webhook-signature";

    private const string JsonContentType = "application/json";

    /// <summary>
    /// Gets how long a stopping replica may take to release the lease of the stream it was delivering.
    /// </summary>
    private static TimeSpan LeaseReleaseTimeout { get; } = TimeSpan.FromSeconds(10);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private ISecretProtectorProvider SecretProtectorProvider { get; } = secretProtectorProvider;

    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;

    private AuditRecordOptions Options { get; } = options.Value;

    private AuthenticationMetrics Metrics { get; } = metrics;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ILogger<DefaultAuditStreamDeliverer> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask DeliverAsync(
        PersistedAuditStreamState state,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(state);

        using var tenantScope = AmbientTenantAccessor.BeginScope(state.TenantId);

        var stream = await ReadStreamAsync(state.StreamId, cancellationToken);
        if (stream?.State is not { } current || !HoldsLease(current, state))
        {
            Logger.AuditStreamStateConflict(state.StreamId, state.TenantId);
            return;
        }

        if (!stream.IsEnabled)
        {
            await TrySaveStateAsync(state, ReleaseLease, cancellationToken);
            return;
        }

        try
        {
            UnprotectedSigningSecrets secrets;
            try
            {
                secrets = await UnprotectCurrentSecretsAsync(stream, cancellationToken);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested
                )
            {
                await RecordFailureAsync(
                    state,
                    SigningSecretUnavailableError,
                    exception,
                    cancellationToken
                );
                return;
            }

            using (secrets)
            {
                var leaseDeadline = GetLeaseDeadline(state);
                var outcome = await DeliverBatchesAsync(
                    state,
                    stream,
                    current,
                    secrets,
                    leaseDeadline,
                    cancellationToken
                );

                // The replica lost the lease, so it has nothing to release.
                if (!outcome.HoldsLease)
                    return;

                await TrySaveStateAsync(
                    state,
                    saved => FinishDelivery(saved, outcome.Error),
                    cancellationToken
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping, so the lease is released for the replica that delivers the stream next.
            await ReleaseOnStopAsync(state);
            throw;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            await RecordFailureAsync(state, DeliveryError, exception, cancellationToken);
        }
    }

    // Counts and logs a failure of this stream's delivery, then backs the stream off like a refused delivery.
    private async ValueTask RecordFailureAsync(
        PersistedAuditStreamState state,
        string error,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        Metrics.AuditStreamFailed();
        Logger.AuditStreamDeliveryErrored(state.StreamId, state.TenantId, exception);
        await TrySaveStateAsync(state, saved => FinishDelivery(saved, error), cancellationToken);
    }

    /// <summary>
    /// Releases the lease of a stream whose delivery the stopping host cancelled, in a unit of work of its own bounded
    /// by <see cref="LeaseReleaseTimeout"/>. A failure to release is logged and not thrown, so that the cancellation of
    /// the host is what propagates.
    /// </summary>
    /// <param name="state">The state of the stream whose lease this replica holds.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    // The stop's own cancellation reaches the host's token but not the release, which runs on a bounded token of its
    // own, so a replica that stops hands its lease back instead of leaving it to expire. See ADR-1534 and ADR-1496.
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The release runs on its own bounded token because the host's token is already cancelled, so no caller can pass a token that drops it (ADR-1534, ADR-1496)."
    )]
    private async ValueTask ReleaseOnStopAsync(PersistedAuditStreamState state)
    {
        using var releaseSource = new CancellationTokenSource(LeaseReleaseTimeout, TimeProvider);
        try
        {
            await TrySaveStateAsync(state, ReleaseLease, releaseSource.Token);
        }
        catch (Exception exception)
        {
            Logger.AuditStreamDeliveryErrored(state.StreamId, state.TenantId, exception);
        }
    }

    private async ValueTask<AuditStreamDeliveryOutcome> DeliverBatchesAsync(
        PersistedAuditStreamState handed,
        PersistedAuditStream stream,
        PersistedAuditStreamState current,
        UnprotectedSigningSecrets secrets,
        DateTimeOffset leaseDeadline,
        CancellationToken cancellationToken
    )
    {
        var positionStoredTime = current.PositionStoredTime;
        var positionEventId = current.PositionEventId;

        while (TimeProvider.GetUtcNow() < leaseDeadline)
        {
            var page = await ReadPageAsync(
                handed.TenantId,
                positionStoredTime,
                positionEventId,
                cancellationToken
            );

            if (
                page.Items.Count == 0
                || page.LastStoredTime is not { } lastStoredTime
                || page.LastEventId is not { } lastEventId
            )
            {
                return new AuditStreamDeliveryOutcome(Error: null, HoldsLease: true);
            }

            var error = await PostAsync(handed.TenantId, stream, page, secrets, cancellationToken);
            if (error is not null)
            {
                Metrics.AuditStreamFailed();
                Logger.AuditStreamDeliveryFailed(handed.StreamId, handed.TenantId, error);
                return new AuditStreamDeliveryOutcome(error, HoldsLease: true);
            }

            var deliveredTime = TimeProvider.GetUtcNow();
            var saved = await TrySaveStateAsync(
                handed,
                state =>
                {
                    state.PositionStoredTime = lastStoredTime;
                    state.PositionEventId = lastEventId;
                    state.ConsecutiveFailures = 0;
                    state.LastDeliveryTime = deliveredTime;
                },
                cancellationToken
            );

            if (!saved)
                return new AuditStreamDeliveryOutcome(Error: null, HoldsLease: false);

            Metrics.AuditStreamDelivered(page.Items.Count);
            positionStoredTime = lastStoredTime;
            positionEventId = lastEventId;

            if (page.Items.Count < Options.StreamBatchSize)
                break;
        }

        return new AuditStreamDeliveryOutcome(Error: null, HoldsLease: true);
    }

    private async ValueTask<PersistedAuditStream?> ReadStreamAsync(
        string streamId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();
        return await store.GetOrDefaultAsync(streamId, cancellationToken);
    }

    private async ValueTask<AuditRecordStreamPage> ReadPageAsync(
        string tenantId,
        DateTimeOffset positionStoredTime,
        Guid positionEventId,
        CancellationToken cancellationToken
    )
    {
        var request = new AuditRecordStreamRequest
        {
            TenantId = tenantId,
            AfterStoredTime = positionStoredTime,
            AfterEventId = positionEventId,
            StoredBefore = TimeProvider.GetUtcNow() - Options.StreamSettleDelay,
            Limit = Options.StreamBatchSize,
        };

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditRecordStore>();
        return await store.GetStreamPageAsync(request, cancellationToken);
    }

    // Writes a state read in this unit of work, never the one the caller holds, which carries a concurrency token that
    // an earlier save has replaced. See ADR-1012. Returns false, and logs, when the stream or the lease is gone.
    private async ValueTask<bool> TrySaveStateAsync(
        PersistedAuditStreamState handed,
        Action<PersistedAuditStreamState> update,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
            var store = storeManager.GetStore<IAuditStreamStore>();

            var stream = await store.GetOrDefaultAsync(handed.StreamId, cancellationToken);
            if (stream?.State is not { } current || !HoldsLease(current, handed))
            {
                Logger.AuditStreamStateConflict(handed.StreamId, handed.TenantId);
                return false;
            }

            update(current);
            await store.UpdateStateAsync(current, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (StoreConcurrencyException)
        {
            Logger.AuditStreamStateConflict(handed.StreamId, handed.TenantId);
            return false;
        }
    }

    private async ValueTask<string?> PostAsync(
        string tenantId,
        PersistedAuditStream stream,
        AuditRecordStreamPage page,
        UnprotectedSigningSecrets secrets,
        CancellationToken cancellationToken
    )
    {
        if (secrets.Count == 0)
            return NoSigningSecretError;

        var timestamp = TimeProvider.GetUtcNow();
        var unixTime = timestamp.ToUnixTimeSeconds();
        var messageId = page.Items[0].EventId.ToString("D");
        var body = AuditStreamPayloadWriter.Write(tenantId, page.Items, timestamp);
        var signatures = new List<string>(secrets.Count);
        for (var index = 0; index < secrets.Count; index++)
        {
            var signature = AuditStreamSignature.Sign(secrets[index], messageId, unixTime, body);
            signatures.Add(signature);
        }

        var signatureHeader = string.Join(' ', signatures);
        var timestampHeader = unixTime.ToString(CultureInfo.InvariantCulture);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, stream.Endpoint)
        {
            Content = content,
        };
        request.Headers.Add(WebhookIdHeader, messageId);
        request.Headers.Add(WebhookTimestampHeader, timestampHeader);
        request.Headers.Add(WebhookSignatureHeader, signatureHeader);

        var httpClient = HttpClientFactory.CreateClient(GuardedHttpClient.Name);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            return response.IsSuccessStatusCode ? null : $"http_{(int)response.StatusCode}";
        }
        catch (HttpRequestException)
        {
            return RequestFailedError;
        }
        // A timeout throws OperationCanceledException with the token untouched; only a cancelled token is shutdown.
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TimeoutError;
        }
    }

    private async ValueTask<UnprotectedSigningSecrets> UnprotectCurrentSecretsAsync(
        PersistedAuditStream stream,
        CancellationToken cancellationToken
    )
    {
        var now = TimeProvider.GetUtcNow();
        var current = stream
            .SigningSecrets.Where(secret => secret.ExpiryTime is null || secret.ExpiryTime > now)
            .ToList();

        var secrets = new UnprotectedSigningSecrets();
        try
        {
            if (current.Count == 0)
                return secrets;

            var owner = SecretOwner.ForTenant(stream.TenantId);
            var protectedValues = current.ConvertAll(secret => secret.ProtectedValue);
            var protector = await SecretProtectorProvider.GetProtectorAsync(
                owner,
                protectedValues,
                cancellationToken
            );

            foreach (var secret in current)
            {
                using var plaintext = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
                IBufferWriter<byte> writer = plaintext;

                var context = SecretProtectionContext.ForAuditStreamSigningSecret(secret.SecretId);
                protector.Unprotect(secret.ProtectedValue, context, ref writer);

                secrets.Add(plaintext.WrittenSpan);
            }

            return secrets;
        }
        catch
        {
            secrets.Dispose();
            throw;
        }
    }

    private DateTimeOffset GetLeaseDeadline(PersistedAuditStreamState state)
    {
        var leaseExpiryTime =
            state.LeaseExpiryTime ?? TimeProvider.GetUtcNow() + Options.StreamLeaseDuration;
        return leaseExpiryTime - Options.StreamLeaseDuration / 2;
    }

    private void FinishDelivery(PersistedAuditStreamState state, string? error)
    {
        ReleaseLease(state);

        var now = TimeProvider.GetUtcNow();
        if (error is null)
        {
            state.NextAttemptTime = now;
            return;
        }

        state.ConsecutiveFailures++;
        state.LastFailureTime = now;
        state.LastError = error;
        state.NextAttemptTime = now + GetRetryDelay(state.ConsecutiveFailures);
    }

    private TimeSpan GetRetryDelay(int consecutiveFailures)
    {
        var delay = Options.StreamRetryDelay;
        for (var failure = 1; failure < consecutiveFailures; failure++)
        {
            if (delay >= Options.StreamMaxRetryDelay)
                break;

            delay *= 2;
        }

        return delay < Options.StreamMaxRetryDelay ? delay : Options.StreamMaxRetryDelay;
    }

    private static void ReleaseLease(PersistedAuditStreamState state)
    {
        state.LeaseHolder = null;
        state.LeaseExpiryTime = null;
    }

    private static bool HoldsLease(
        PersistedAuditStreamState current,
        PersistedAuditStreamState handed
    ) => string.Equals(current.LeaseHolder, handed.LeaseHolder, StringComparison.Ordinal);
}
