// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.OpenId.Authentication.OutboundHttp;
using NCode.Identity.OpenId.Core.Tests.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public sealed class DefaultAuditStreamDelivererTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string StreamId = "stream-1";
    private const string Endpoint = "https://receiver.example/audit";
    private const string ReplicaId = "replica-1";

    private static DateTimeOffset Now { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static byte[] CurrentSecretBytes { get; } = [1, 2, 3, 4, 5, 6, 7, 8];
    private static byte[] RotatedSecretBytes { get; } = [9, 10, 11, 12, 13, 14, 15, 16];
    private static byte[] ExpiredSecretBytes { get; } = [17, 18, 19, 20, 21, 22, 23, 24];

    private FakeTimeProvider TimeProvider { get; } = new(Now);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ISecretProtectorProvider> MockSecretProtectorProvider { get; }
    private Mock<IHttpClientFactory> MockHttpClientFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IAuditStreamStore> MockStreamStore { get; }
    private Mock<IAuditRecordStore> MockRecordStore { get; }
    private FakeSecretProtector Protector { get; }
    private StubHttpMessageHandler Handler { get; } = new();
    private FakeLogger<DefaultAuditStreamDeliverer> Logger { get; } = new();
    private ServiceProvider MetricsProvider { get; }
    private MetricCollector<long> DeliveryCollector { get; }
    private MetricCollector<long> RecordCollector { get; }
    private AuditRecordOptions RecordOptions { get; } = new() { StreamBatchSize = 2 };
    private DefaultAuditStreamDeliverer Deliverer { get; }

    // The state as the store holds it; every read returns a copy, as a read returns a fresh state with a fresh token.
    private PersistedAuditStreamState Stored { get; set; }
    private PersistedAuditStream StoredStream { get; set; }
    private bool RecordStoreIsSetUp { get; set; }

    public DefaultAuditStreamDelivererTests()
    {
        MetricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = MetricsProvider.GetRequiredService<IMeterFactory>();
        DeliveryCollector = new MetricCollector<long>(
            meterFactory,
            OpenIdAuthenticationMetrics.MeterName,
            AuthenticationMetrics.AuditStreamDeliveriesName
        );
        RecordCollector = new MetricCollector<long>(
            meterFactory,
            OpenIdAuthenticationMetrics.MeterName,
            AuthenticationMetrics.AuditStreamRecordsName
        );

        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockSecretProtectorProvider = CreateStrictMock<ISecretProtectorProvider>();
        MockHttpClientFactory = CreateStrictMock<IHttpClientFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockStreamStore = CreateStrictMock<IAuditStreamStore>();
        MockRecordStore = CreateStrictMock<IAuditRecordStore>();
        Protector = new FakeSecretProtector(SecretOwner.ForTenant(TenantId));

        Stored = CreateState();
        StoredStream = CreateStream(
            isEnabled: true,
            CreateSecret("current", CurrentSecretBytes, expiryTime: null),
            CreateSecret("rotated", RotatedSecretBytes, expiryTime: Now.AddHours(1)),
            CreateSecret("expired", ExpiredSecretBytes, expiryTime: Now.AddHours(-1))
        );

        Deliverer = new DefaultAuditStreamDeliverer(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockSecretProtectorProvider.Object,
            MockHttpClientFactory.Object,
            Options.Create(RecordOptions),
            new AuthenticationMetrics(meterFactory),
            TimeProvider,
            Logger
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DeliveryCollector.Dispose();
            RecordCollector.Dispose();
            MetricsProvider.Dispose();
            Handler.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<byte[]> Bodies { get; } = [];

        public List<string?> MediaTypes { get; } = [];

        public void Enqueue(HttpStatusCode statusCode) =>
            Responses.Enqueue(() => new HttpResponseMessage(statusCode));

        public void Enqueue(HttpStatusCode statusCode, Action onSend) =>
            Responses.Enqueue(() =>
            {
                onSend();
                return new HttpResponseMessage(statusCode);
            });

        public void EnqueueFailure(Exception exception) => Responses.Enqueue(() => throw exception);

        public void EnqueueCancellation(CancellationTokenSource source) =>
            Responses.Enqueue(() =>
            {
                source.Cancel();
                throw new OperationCanceledException(source.Token);
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            MediaTypes.Add(request.Content?.Headers.ContentType?.MediaType);
            Bodies.Add(
                request.Content is null
                    ? []
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken)
            );
            return Responses.Dequeue()();
        }
    }

    #region Scaffolding

    private static PersistedAuditStreamState CreateState(
        string? leaseHolder = ReplicaId,
        int consecutiveFailures = 0
    ) =>
        new()
        {
            TenantId = TenantId,
            StreamId = StreamId,
            PositionStoredTime = Now.AddHours(-1),
            PositionEventId = Guid.Empty,
            NextAttemptTime = Now.AddMinutes(-1),
            ConsecutiveFailures = consecutiveFailures,
            LeaseHolder = leaseHolder,
            LeaseExpiryTime = Now.AddMinutes(2),
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
        };

    private static PersistedAuditStreamState Copy(PersistedAuditStreamState state) =>
        new()
        {
            TenantId = state.TenantId,
            StreamId = state.StreamId,
            PositionStoredTime = state.PositionStoredTime,
            PositionEventId = state.PositionEventId,
            NextAttemptTime = state.NextAttemptTime,
            ConsecutiveFailures = state.ConsecutiveFailures,
            LastDeliveryTime = state.LastDeliveryTime,
            LastFailureTime = state.LastFailureTime,
            LastError = state.LastError,
            LeaseHolder = state.LeaseHolder,
            LeaseExpiryTime = state.LeaseExpiryTime,
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
        };

    private PersistedAuditStreamSigningSecret CreateSecret(
        string secretId,
        byte[] bytes,
        DateTimeOffset? expiryTime
    )
    {
        var protectedValue = "fake." + Convert.ToBase64String(bytes);
        Protector.Contexts[protectedValue] = SecretProtectionContext.ForAuditStreamSigningSecret(
            secretId
        );
        return new PersistedAuditStreamSigningSecret
        {
            SecretId = secretId,
            ProtectedValue = protectedValue,
            CreatedTime = Now.AddDays(-1),
            ExpiryTime = expiryTime,
        };
    }

    private static PersistedAuditStream CreateStream(
        bool isEnabled,
        params PersistedAuditStreamSigningSecret[] secrets
    ) =>
        new()
        {
            TenantId = TenantId,
            StreamId = StreamId,
            DisplayName = "Stream",
            Endpoint = Endpoint,
            IsEnabled = isEnabled,
            CreatedTime = Now.AddDays(-2),
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
            SigningSecrets = secrets,
        };

    private static PersistedAuditRecord CreateRecord(Guid eventId) =>
        new()
        {
            EventId = eventId,
            EventTime = Now.AddMinutes(-10),
            EventName = "token.issued",
            EventCategory = "token",
            Outcome = "success",
            Severity = "info",
            Cell = "cell-1",
            TenantId = TenantId,
            ActorType = "client",
            Resources = [],
            Record = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                "{}"
            ),
        };

    private static AuditRecordStreamPage CreatePage(
        DateTimeOffset lastStoredTime,
        params PersistedAuditRecord[] records
    ) =>
        new()
        {
            Items = records,
            LastStoredTime = records.Length == 0 ? null : lastStoredTime,
            LastEventId = records.Length == 0 ? null : records[^1].EventId,
        };

    private void SetupScope()
    {
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(Mock.Of<IDisposable>())
            .Verifiable();
    }

    private void SetupStores(int updates, Exception? readFailure = null)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable(Times.AtLeastOnce);
        MockStoreManager
            .Setup(x => x.GetStore<IAuditStreamStore>())
            .Returns(MockStreamStore.Object)
            .Verifiable(Times.AtLeastOnce);
        MockStoreManager
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.AtLeastOnce);
        var read = MockStreamStore.Setup(x =>
            x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>())
        );
        if (readFailure is null)
        {
            read.ReturnsAsync(() => CopyOfStoredStream()).Verifiable(Times.Once);
        }
        else
        {
            read.ThrowsAsync(readFailure).Verifiable(Times.Once);
        }
        // Each state write reads the state alone; the counted updates verify the writes.
        MockStreamStore
            .Setup(x => x.GetStateOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Copy(Stored));

        if (updates == 0)
            return;

        MockStreamStore
            .Setup(x =>
                x.UpdateStateAsync(
                    It.IsAny<PersistedAuditStreamState>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (PersistedAuditStreamState state, CancellationToken _) => Stored = Copy(state)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(updates));
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(updates));
    }

    private PersistedAuditStream CopyOfStoredStream() =>
        new()
        {
            TenantId = StoredStream.TenantId,
            StreamId = StoredStream.StreamId,
            DisplayName = StoredStream.DisplayName,
            Endpoint = StoredStream.Endpoint,
            IsEnabled = StoredStream.IsEnabled,
            CreatedTime = StoredStream.CreatedTime,
            ConcurrencyToken = StoredStream.ConcurrencyToken,
            SigningSecrets = StoredStream.SigningSecrets,
            State = Copy(Stored),
        };

    private void SetupProtector() =>
        MockSecretProtectorProvider
            .Setup(x =>
                x.GetProtectorAsync(
                    SecretOwner.ForTenant(TenantId),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<ISecretProtector>(Protector))
            .Verifiable();

    private void SetupHttpClient() =>
        MockHttpClientFactory
            .Setup(x => x.CreateClient(GuardedHttpClient.Name))
            .Returns(() => new HttpClient(Handler, disposeHandler: false))
            .Verifiable(Times.AtLeastOnce);

    private AuditRecordStreamRequest CreateRequest(
        DateTimeOffset afterStoredTime,
        Guid afterEventId,
        DateTimeOffset? readTime = null
    ) =>
        new()
        {
            TenantId = TenantId,
            AfterStoredTime = afterStoredTime,
            AfterEventId = afterEventId,
            StoredBefore = (readTime ?? Now) - RecordOptions.StreamSettleDelay,
            Limit = RecordOptions.StreamBatchSize,
        };

    private void SetupRecordStore()
    {
        if (RecordStoreIsSetUp)
            return;

        RecordStoreIsSetUp = true;
        MockStoreManager
            .Setup(x => x.GetStore<IAuditRecordStore>())
            .Returns(MockRecordStore.Object)
            .Verifiable(Times.AtLeastOnce);
    }

    private void SetupPage(
        DateTimeOffset afterStoredTime,
        Guid afterEventId,
        AuditRecordStreamPage page,
        DateTimeOffset? readTime = null
    )
    {
        SetupRecordStore();

        MockRecordStore
            .Setup(x =>
                x.GetStreamPageAsync(
                    CreateRequest(afterStoredTime, afterEventId, readTime),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(page)
            .Verifiable();
    }

    private void SetupPageFailure(
        DateTimeOffset afterStoredTime,
        Guid afterEventId,
        Exception exception
    )
    {
        SetupRecordStore();

        MockRecordStore
            .Setup(x =>
                x.GetStreamPageAsync(
                    CreateRequest(afterStoredTime, afterEventId),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(exception)
            .Verifiable();
    }

    private static string Header(HttpRequestMessage request, string name) =>
        Assert.Single(request.Headers.GetValues(name));

    #endregion

    #region DeliverAsync Tests

    [Fact]
    public async Task DeliverAsync_WhenTheEndpointAccepts_MovesThePositionAndCounts()
    {
        var storedTime = Now.AddMinutes(-5);
        var record = CreateRecord(Guid.CreateVersion7());
        Stored = CreateState(consecutiveFailures: 3);
        SetupScope();
        SetupStores(updates: 2);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, CreatePage(storedTime, record));
        Handler.Enqueue(HttpStatusCode.OK);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Single(Handler.Requests);
        Assert.Equal(new Uri(Endpoint), Handler.Requests[0].RequestUri);
        Assert.Equal(storedTime, Stored.PositionStoredTime);
        Assert.Equal(record.EventId, Stored.PositionEventId);
        Assert.Equal(0, Stored.ConsecutiveFailures);
        Assert.Equal(Now, Stored.LastDeliveryTime);
        Assert.Equal(Now, Stored.NextAttemptTime);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
        var delivery = Assert.Single(DeliveryCollector.GetMeasurementSnapshot());
        Assert.Equal(1, delivery.Value);
        Assert.Equal(
            AuthenticationMetrics.DeliveredResult,
            delivery.Tags.GetValueOrDefault(AuthenticationMetrics.AuditStreamResultAttribute)
        );
        Assert.Equal(1, Assert.Single(RecordCollector.GetMeasurementSnapshot()).Value);
    }

    [Fact]
    public async Task DeliverAsync_Always_SignsWithEveryCurrentSecret()
    {
        var record = CreateRecord(Guid.CreateVersion7());
        SetupScope();
        SetupStores(updates: 2);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, CreatePage(Now.AddMinutes(-5), record));
        Handler.Enqueue(HttpStatusCode.NoContent);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        var request = Assert.Single(Handler.Requests);
        var messageId = record.EventId.ToString("D");
        var timestamp = Now.ToUnixTimeSeconds();
        Assert.Equal(messageId, Header(request, "webhook-id"));
        Assert.Equal(
            timestamp.ToString(CultureInfo.InvariantCulture),
            Header(request, "webhook-timestamp")
        );
        var body = Handler.Bodies[0];
        var expected = string.Join(
            ' ',
            AuditStreamSignature.Sign(CurrentSecretBytes, messageId, timestamp, body),
            AuditStreamSignature.Sign(RotatedSecretBytes, messageId, timestamp, body)
        );
        Assert.Equal(expected, Header(request, "webhook-signature"));
        Assert.DoesNotContain(
            AuditStreamSignature.Sign(ExpiredSecretBytes, messageId, timestamp, body),
            Header(request, "webhook-signature"),
            StringComparison.Ordinal
        );
        Assert.Equal("application/json", Assert.Single(Handler.MediaTypes));
    }

    [Fact]
    public async Task DeliverAsync_WhenTheEndpointRefuses_KeepsThePositionAndBacksOff()
    {
        var position = Stored.PositionStoredTime;
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            position,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.Enqueue(HttpStatusCode.InternalServerError);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal(position, Stored.PositionStoredTime);
        Assert.Equal(Guid.Empty, Stored.PositionEventId);
        Assert.Equal(1, Stored.ConsecutiveFailures);
        Assert.Equal("http_500", Stored.LastError);
        Assert.Equal(Now, Stored.LastFailureTime);
        Assert.Equal(Now.AddSeconds(10), Stored.NextAttemptTime);
        Assert.Null(Stored.LeaseHolder);
        var delivery = Assert.Single(DeliveryCollector.GetMeasurementSnapshot());
        Assert.Equal(
            AuthenticationMetrics.FailedResult,
            delivery.Tags.GetValueOrDefault(AuthenticationMetrics.AuditStreamResultAttribute)
        );
        Assert.Empty(RecordCollector.GetMeasurementSnapshot());
        var logged = Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamDeliveryFailed
        );
        Assert.Equal("http_500", logged.GetStructuredStateValue("Error"));
        Assert.Null(logged.Exception);
    }

    [Fact]
    public async Task DeliverAsync_WhenFailuresRepeat_DoublesTheBackoffUpToTheMaximum()
    {
        Stored = CreateState(consecutiveFailures: 9);
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.Enqueue(HttpStatusCode.BadGateway);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal(10, Stored.ConsecutiveFailures);
        Assert.Equal(Now + RecordOptions.StreamMaxRetryDelay, Stored.NextAttemptTime);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheRequestFails_RecordsRequestFailed()
    {
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.EnqueueFailure(new HttpRequestException("The connection was reset."));

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal("request_failed", Stored.LastError);
        Assert.Equal(1, Stored.ConsecutiveFailures);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheRequestFails_LogsTheExceptionWithTheError()
    {
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.EnqueueFailure(new HttpRequestException("The connection was reset."));

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        var logged = Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamDeliveryFailed
        );
        Assert.Equal("request_failed", logged.GetStructuredStateValue("Error"));
        var exception = Assert.IsType<HttpRequestException>(logged.Exception);
        Assert.Equal("The connection was reset.", exception.Message);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheRequestTimesOut_RecordsTimeout()
    {
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.EnqueueFailure(new TaskCanceledException("The request timed out."));

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal("timeout", Stored.LastError);
    }

    [Fact]
    public async Task DeliverAsync_WithoutACurrentSecret_RecordsNoSigningSecret()
    {
        StoredStream = CreateStream(
            isEnabled: true,
            CreateSecret("expired", ExpiredSecretBytes, expiryTime: Now.AddHours(-1))
        );
        SetupScope();
        SetupStores(updates: 1);
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Empty(Handler.Requests);
        Assert.Equal("no_signing_secret", Stored.LastError);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheSigningSecretsCannotBeUnprotected_RecordsTheFailureAndReleasesTheLease()
    {
        var position = Stored.PositionStoredTime;
        var failure = new CryptographicException("The master key cannot unwrap the secret.");
        SetupScope();
        SetupStores(updates: 1);
        MockSecretProtectorProvider
            .Setup(x =>
                x.GetProtectorAsync(
                    SecretOwner.ForTenant(TenantId),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(failure)
            .Verifiable();

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Empty(Handler.Requests);
        Assert.Equal(position, Stored.PositionStoredTime);
        Assert.Equal(1, Stored.ConsecutiveFailures);
        Assert.Equal("signing_secret_unavailable", Stored.LastError);
        Assert.Equal(Now, Stored.LastFailureTime);
        Assert.Equal(Now + RecordOptions.StreamRetryDelay, Stored.NextAttemptTime);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
        var logged = Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamDeliveryErrored
        );
        Assert.Equal(StreamId, logged.GetStructuredStateValue("StreamId"));
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheSecretFailureCannotBeSaved_CountsOneFailureAndThrows()
    {
        var failure = new CryptographicException("The master key cannot unwrap the secret.");
        SetupScope();
        SetupStores(updates: 0);
        MockSecretProtectorProvider
            .Setup(x =>
                x.GetProtectorAsync(
                    SecretOwner.ForTenant(TenantId),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(failure)
            .Verifiable();
        MockStreamStore
            .Setup(x =>
                x.UpdateStateAsync(
                    It.IsAny<PersistedAuditStreamState>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(new InvalidOperationException("unreachable"))
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None).AsTask()
        );

        var delivery = Assert.Single(DeliveryCollector.GetMeasurementSnapshot());
        Assert.Equal(
            AuthenticationMetrics.FailedResult,
            delivery.Tags.GetValueOrDefault(AuthenticationMetrics.AuditStreamResultAttribute)
        );
        Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamDeliveryErrored
        );
    }

    [Fact]
    public async Task DeliverAsync_WhenTheFinalSaveFails_CountsNoFailureAndThrows()
    {
        var record = CreateRecord(Guid.CreateVersion7());
        var updateCalls = 0;
        SetupScope();
        SetupStores(updates: 0);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, CreatePage(Now.AddMinutes(-5), record));
        Handler.Enqueue(HttpStatusCode.OK);
        MockStreamStore
            .Setup(x =>
                x.UpdateStateAsync(
                    It.IsAny<PersistedAuditStreamState>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (PersistedAuditStreamState state, CancellationToken _) =>
                {
                    if (++updateCalls > 1)
                        throw new InvalidOperationException("unreachable");

                    Stored = Copy(state);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None).AsTask()
        );

        var delivery = Assert.Single(DeliveryCollector.GetMeasurementSnapshot());
        Assert.Equal(
            AuthenticationMetrics.DeliveredResult,
            delivery.Tags.GetValueOrDefault(AuthenticationMetrics.AuditStreamResultAttribute)
        );
        Assert.DoesNotContain(
            Logger.Collector.GetSnapshot(),
            logged => logged.Id.Id == EventIds.AuditStreamDeliveryErrored
        );
    }

    [Fact]
    public async Task DeliverAsync_WhenTheStoreFailsMidDelivery_RecordsADeliveryError()
    {
        var position = Stored.PositionStoredTime;
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupPageFailure(position, Guid.Empty, new InvalidOperationException("unreachable"));

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Empty(Handler.Requests);
        Assert.Equal(position, Stored.PositionStoredTime);
        Assert.Equal(1, Stored.ConsecutiveFailures);
        Assert.Equal("delivery_error", Stored.LastError);
        Assert.Equal(Now + RecordOptions.StreamRetryDelay, Stored.NextAttemptTime);
        Assert.Null(Stored.LeaseHolder);
        Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamDeliveryErrored
        );
    }

    [Fact]
    public async Task DeliverAsync_WhenTheHostStops_ReleasesTheLeaseAndRethrows()
    {
        var position = Stored.PositionStoredTime;
        using var cancellationSource = new CancellationTokenSource();
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            position,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.EnqueueCancellation(cancellationSource);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Deliverer.DeliverAsync(Copy(Stored), cancellationSource.Token).AsTask()
        );

        Assert.Equal(position, Stored.PositionStoredTime);
        Assert.Equal(0, Stored.ConsecutiveFailures);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheHostStopsDuringTheFirstRead_ReleasesTheLeaseAndRethrows()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        SetupScope();
        SetupStores(
            updates: 1,
            readFailure: new OperationCanceledException(cancellationSource.Token)
        );
        var handed = Copy(Stored);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Deliverer.DeliverAsync(handed, cancellationSource.Token).AsTask()
        );

        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
    }

    [Fact]
    public async Task DeliverAsync_WhenNothingIsStored_ReleasesTheLease()
    {
        var position = Stored.PositionStoredTime;
        SetupScope();
        SetupStores(updates: 1);
        SetupProtector();
        SetupPage(position, Guid.Empty, CreatePage(Now));

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Empty(Handler.Requests);
        Assert.Equal(position, Stored.PositionStoredTime);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
        Assert.Equal(Now, Stored.NextAttemptTime);
        Assert.Empty(DeliveryCollector.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task DeliverAsync_ForADisabledStream_PostsNothing()
    {
        StoredStream = CreateStream(isEnabled: false);
        SetupScope();
        SetupStores(updates: 1);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Empty(Handler.Requests);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
    }

    [Fact]
    public async Task DeliverAsync_WhenAFullBatchIsAccepted_SendsTheNextBatch()
    {
        var firstStoredTime = Now.AddMinutes(-6);
        var secondStoredTime = Now.AddMinutes(-5);
        var first = CreatePage(
            firstStoredTime,
            CreateRecord(Guid.CreateVersion7()),
            CreateRecord(Guid.CreateVersion7())
        );
        var second = CreatePage(secondStoredTime, CreateRecord(Guid.CreateVersion7()));
        SetupScope();
        SetupStores(updates: 3);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, first);
        SetupPage(firstStoredTime, first.LastEventId.GetValueOrDefault(), second);
        Handler.Enqueue(HttpStatusCode.OK);
        Handler.Enqueue(HttpStatusCode.OK);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal(2, Handler.Requests.Count);
        Assert.Equal(secondStoredTime, Stored.PositionStoredTime);
        Assert.Equal(second.LastEventId, Stored.PositionEventId);
        Assert.Equal(2, DeliveryCollector.GetMeasurementSnapshot().Count);
        Assert.Equal(3, RecordCollector.GetMeasurementSnapshot().Sum(m => m.Value));
    }

    [Fact]
    public async Task DeliverAsync_WhenHalfTheLeaseIsSpent_StopsAfterTheBatchItSent()
    {
        var firstStoredTime = Now.AddMinutes(-6);
        var first = CreatePage(
            firstStoredTime,
            CreateRecord(Guid.CreateVersion7()),
            CreateRecord(Guid.CreateVersion7())
        );
        var halfLease = RecordOptions.StreamLeaseDuration / 2;
        var pastHalf = TimeSpan.FromSeconds(1);
        SetupScope();
        SetupStores(updates: 2);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, first);
        Handler.Enqueue(
            HttpStatusCode.OK,
            () => TimeProvider.Advance(Stored.LeaseExpiryTime!.Value - Now - halfLease + pastHalf)
        );

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Single(Handler.Requests);
        Assert.Equal(firstStoredTime, Stored.PositionStoredTime);
        Assert.Equal(first.LastEventId, Stored.PositionEventId);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
        Assert.Equal(Now + halfLease + pastHalf, Stored.NextAttemptTime);
        Assert.Equal(1, Assert.Single(DeliveryCollector.GetMeasurementSnapshot()).Value);
    }

    [Fact]
    public async Task DeliverAsync_WhileHalfTheLeaseRemains_SendsTheNextFullBatch()
    {
        var firstStoredTime = Now.AddMinutes(-6);
        var secondStoredTime = Now.AddMinutes(-5);
        var first = CreatePage(
            firstStoredTime,
            CreateRecord(Guid.CreateVersion7()),
            CreateRecord(Guid.CreateVersion7())
        );
        var second = CreatePage(secondStoredTime, CreateRecord(Guid.CreateVersion7()));
        var halfLease = RecordOptions.StreamLeaseDuration / 2;
        var elapsed = halfLease - TimeSpan.FromSeconds(1);
        SetupScope();
        SetupStores(updates: 3);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, first);
        SetupPage(
            firstStoredTime,
            first.LastEventId.GetValueOrDefault(),
            second,
            readTime: Now + elapsed
        );
        Handler.Enqueue(HttpStatusCode.OK, () => TimeProvider.Advance(elapsed));
        Handler.Enqueue(HttpStatusCode.OK);

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Equal(2, Handler.Requests.Count);
        Assert.Equal(secondStoredTime, Stored.PositionStoredTime);
        Assert.Equal(second.LastEventId, Stored.PositionEventId);
        Assert.Null(Stored.LeaseHolder);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheHandedStateHasNoLeaseExpiry_StopsHalfAFullLeaseFromNow()
    {
        var firstStoredTime = Now.AddMinutes(-6);
        var first = CreatePage(
            firstStoredTime,
            CreateRecord(Guid.CreateVersion7()),
            CreateRecord(Guid.CreateVersion7())
        );
        var halfLease = RecordOptions.StreamLeaseDuration / 2;
        var handed = Copy(Stored);
        handed.LeaseExpiryTime = null;
        SetupScope();
        SetupStores(updates: 2);
        SetupProtector();
        SetupHttpClient();
        SetupPage(Stored.PositionStoredTime, Guid.Empty, first);
        Handler.Enqueue(HttpStatusCode.OK, () => TimeProvider.Advance(halfLease));

        await Deliverer.DeliverAsync(handed, CancellationToken.None);

        Assert.Single(Handler.Requests);
        Assert.Equal(firstStoredTime, Stored.PositionStoredTime);
        Assert.Null(Stored.LeaseHolder);
        Assert.Null(Stored.LeaseExpiryTime);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheStateChanged_LogsTheConflict()
    {
        SetupScope();
        SetupStores(updates: 0);
        SetupProtector();
        SetupHttpClient();
        SetupPage(
            Stored.PositionStoredTime,
            Guid.Empty,
            CreatePage(Now.AddMinutes(-5), CreateRecord(Guid.CreateVersion7()))
        );
        Handler.Enqueue(HttpStatusCode.OK);
        MockStreamStore
            .Setup(x =>
                x.UpdateStateAsync(
                    It.IsAny<PersistedAuditStreamState>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        MockStoreManager.SetupSaveChangesFailingOnce(
            new StoreConcurrencyException("conflict"),
            Times.Once()
        );

        await Deliverer.DeliverAsync(Copy(Stored), CancellationToken.None);

        Assert.Single(Handler.Requests);
        Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamStateConflict
        );
        Assert.Empty(RecordCollector.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task DeliverAsync_WhenAnotherReplicaHoldsTheLease_StopsWithoutDelivering()
    {
        Stored = CreateState(leaseHolder: "replica-2");
        SetupScope();
        SetupStores(updates: 0);

        await Deliverer.DeliverAsync(
            Copy(CreateState(leaseHolder: ReplicaId)),
            CancellationToken.None
        );

        Assert.Empty(Handler.Requests);
        Assert.Equal("replica-2", Stored.LeaseHolder);
        Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamStateConflict
        );
    }

    #endregion
}
