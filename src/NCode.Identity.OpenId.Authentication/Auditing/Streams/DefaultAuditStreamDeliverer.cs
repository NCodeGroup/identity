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

    /// <summary>
    /// Clears the lease holder and the lease expiry of a delivery state.
    /// </summary>
    /// <param name="state">The state whose lease is cleared.</param>
    private static void ReleaseLease(PersistedAuditStreamState state)
    {
        state.LeaseHolder = null;
        state.LeaseExpiryTime = null;
    }

    /// <summary>
    /// Determines whether the lease holder of the current state is the one that this replica claimed.
    /// </summary>
    /// <param name="current">The state as it is stored now.</param>
    /// <param name="handed">The state as this replica claimed it.</param>
    /// <returns><see langword="true"/> when both name the same lease holder; otherwise
    /// <see langword="false"/>.</returns>
    private static bool HoldsLease(
        PersistedAuditStreamState current,
        PersistedAuditStreamState handed
    ) => string.Equals(current.LeaseHolder, handed.LeaseHolder, StringComparison.Ordinal);

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
            UnprotectedSigningSecrets unprotected;
            try
            {
                unprotected = await UnprotectCurrentSecretsAsync(stream, cancellationToken);
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

            using var secrets = unprotected;

            AuditStreamDeliveryOutcome outcome;
            try
            {
                var leaseDeadline = GetLeaseDeadline(state);
                outcome = await DeliverBatchesAsync(
                    state,
                    stream,
                    current,
                    secrets,
                    leaseDeadline,
                    cancellationToken
                );
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested
                )
            {
                await RecordFailureAsync(state, DeliveryError, exception, cancellationToken);
                return;
            }

            // The replica lost the lease, so it has nothing to release.
            if (!outcome.HoldsLease)
                return;

            // A failure to save the finished state is not a failed delivery: it reaches the caller, which logs it.
            await TrySaveStateAsync(
                state,
                saved => FinishDelivery(saved, outcome.Error),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping, so the lease is released for the replica that delivers the stream next.
            await ReleaseOnStopAsync(state);
            throw;
        }
    }

    /// <summary>
    /// Counts and logs a failure of this stream's delivery, then backs the stream off like a refused delivery.
    /// </summary>
    /// <param name="state">The state of the stream as this replica claimed it.</param>
    /// <param name="error">The short error code that names why the delivery failed.</param>
    /// <param name="exception">The exception that failed the delivery.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
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
    /// by a short timeout. A failure to release is logged and not thrown, so that the cancellation of the host is what
    /// propagates.
    /// </summary>
    /// <param name="state">The state of the stream whose lease this replica holds.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    // The release runs on a token bounded by LeaseReleaseTimeout. The stop's own cancellation reaches the host's token
    // but not the release, which runs on a bounded token of its own, so a replica that stops hands its lease back
    // instead of leaving it to expire. See ADR-1534 and ADR-1496.
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

    /// <summary>
    /// Posts the stored audit records after the stream's position, one page at a time, until no record is left to send,
    /// a post is refused, the replica loses its lease, or the lease deadline passes. Each delivered page moves the
    /// stream's position past its last record.
    /// </summary>
    /// <param name="handed">The state of the stream as this replica claimed it.</param>
    /// <param name="stream">The stream to deliver.</param>
    /// <param name="current">The state of the stream as it was read when the delivery began.</param>
    /// <param name="secrets">The unprotected signing secrets that sign each post.</param>
    /// <param name="leaseDeadline">The time after which no further page is started.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="AuditStreamDeliveryOutcome"/> that holds the error that stopped the delivery, if any, and whether
    /// this replica still holds the lease.
    /// </returns>
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

    /// <summary>
    /// Reads an audit stream, with its state and signing secrets, in a unit of work of its own.
    /// </summary>
    /// <param name="streamId">The identifier of the stream to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The stream, or <see langword="null"/> when it no longer exists.</returns>
    private async ValueTask<PersistedAuditStream?> ReadStreamAsync(
        string streamId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IAuditStreamStore>();
        return await store.GetOrDefaultAsync(streamId, cancellationToken);
    }

    /// <summary>
    /// Reads the next page of a tenant's audit records after a stream position, leaving out the records stored too
    /// recently to have settled.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant whose records are read.</param>
    /// <param name="positionStoredTime">The stored time of the last record already delivered.</param>
    /// <param name="positionEventId">The event identifier of the last record already delivered.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The page of up to one batch of records, which is empty when none is left.</returns>
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

    /// <summary>
    /// Applies a change to the delivery state of a stream that this replica holds the lease of. The change is written
    /// to a state read in this unit of work, never to the one the caller holds, which carries a concurrency token that
    /// an earlier save has replaced.
    /// </summary>
    /// <param name="handed">The state of the stream as this replica claimed it.</param>
    /// <param name="update">The change to apply to the state that was read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="true"/> when the change was saved; otherwise <see langword="false"/>, after logging, when the
    /// stream or the lease is gone.
    /// </returns>
    // See ADR-1012.
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

    /// <summary>
    /// Posts a page of audit records to the stream's endpoint as a signed JSON payload, carrying one signature for each
    /// signing secret.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant that owns the records.</param>
    /// <param name="stream">The stream whose endpoint receives the post.</param>
    /// <param name="page">The page of records to send.</param>
    /// <param name="secrets">The unprotected signing secrets that sign the post.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="null"/> when the endpoint accepted the post; otherwise the short error code that names why it did
    /// not.
    /// </returns>
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
        using var body = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);
        AuditStreamPayloadWriter.Write(body, tenantId, page.Items, timestamp);
        var signatures = new List<string>(secrets.Count);
        for (var index = 0; index < secrets.Count; index++)
        {
            var signature = AuditStreamSignature.Sign(
                secrets[index],
                messageId,
                unixTime,
                body.WrittenSpan
            );
            signatures.Add(signature);
        }

        var signatureHeader = string.Join(' ', signatures);
        var timestampHeader = unixTime.ToString(CultureInfo.InvariantCulture);

        using var content = new ReadOnlyMemoryContent(body.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, stream.Endpoint)
        {
            Content = content,
        };
        request.Headers.Add(WebhookIdHeader, messageId);
        request.Headers.Add(WebhookTimestampHeader, timestampHeader);
        request.Headers.Add(WebhookSignatureHeader, signatureHeader);

        using var httpClient = HttpClientFactory.CreateClient(GuardedHttpClient.Name);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            if (response.IsSuccessStatusCode)
                return null;

            var statusCode = (int)response.StatusCode;
            return string.Create(CultureInfo.InvariantCulture, $"http_{statusCode}");
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

    /// <summary>
    /// Unprotects the signing secrets of a stream that have not expired.
    /// </summary>
    /// <param name="stream">The stream whose signing secrets are unprotected.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The unprotected signing secrets, which the caller disposes, and which are empty when every secret has
    /// expired.</returns>
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

    /// <summary>
    /// Gets the time after which a delivery starts no further page, which is half a lease duration before the lease
    /// expires so that the last page finishes while the lease is still held.
    /// </summary>
    /// <param name="state">The state of the stream whose lease is held.</param>
    /// <returns>The time after which no further page is started.</returns>
    private DateTimeOffset GetLeaseDeadline(PersistedAuditStreamState state)
    {
        var leaseExpiryTime =
            state.LeaseExpiryTime ?? TimeProvider.GetUtcNow() + Options.StreamLeaseDuration;
        return leaseExpiryTime - Options.StreamLeaseDuration / 2;
    }

    /// <summary>
    /// Releases the lease of a stream and schedules its next attempt: at once after a delivery that succeeded, and
    /// after a growing delay, with the failure recorded, after one that failed.
    /// </summary>
    /// <param name="state">The state of the stream whose delivery ended.</param>
    /// <param name="error">The error that ended the delivery, or <see langword="null"/> when it succeeded.</param>
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

    /// <summary>
    /// Gets how long to wait before the next attempt, which starts at the configured retry delay, doubles with each
    /// further consecutive failure, and never exceeds the configured maximum.
    /// </summary>
    /// <param name="consecutiveFailures">The number of deliveries in a row that have failed.</param>
    /// <returns>The delay before the next attempt.</returns>
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
}
