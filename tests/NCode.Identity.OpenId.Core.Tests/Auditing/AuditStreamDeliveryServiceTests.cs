// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public sealed class AuditStreamDeliveryServiceTests : BaseTests
{
    private const string UnscopedReason = "audit stream delivery";

    private static DateTimeOffset Now { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private FakeTimeProvider TimeProvider { get; } = new(Now);
    private FakeLogger<AuditStreamDeliveryService> Logger { get; } = new();
    private AuditRecordOptions AuditOptions { get; } = new();
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IAuditStreamDeliverer> MockDeliverer { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IAuditStreamStore> MockAuditStreamStore { get; }
    private AuditStreamDeliveryService Service { get; }

    public AuditStreamDeliveryServiceTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockDeliverer = CreateStrictMock<IAuditStreamDeliverer>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockAuditStreamStore = CreateStrictMock<IAuditStreamStore>();
        Service = CreateService(AuditOptions);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Service.Dispose();
        }

        base.Dispose(disposing);
    }

    private AuditStreamDeliveryService CreateService(AuditRecordOptions options) =>
        new(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockDeliverer.Object,
            Options.Create(options),
            TimeProvider,
            Logger
        );

    #region Scaffolding

    private static PersistedAuditStreamState CreateState(string streamId) =>
        new()
        {
            TenantId = "tenant-1",
            StreamId = streamId,
            PositionStoredTime = Now.AddHours(-1),
            PositionEventId = Guid.Empty,
            NextAttemptTime = Now.AddMinutes(-1),
            ConsecutiveFailures = 0,
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
        };

    private void SetupUnscoped()
    {
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(Mock.Of<IDisposable>())
            .Verifiable();
    }

    private void SetupStores()
    {
        SetupUnscoped();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IAuditStreamStore>())
            .Returns(MockAuditStreamStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupDue(
        IReadOnlyList<PersistedAuditStreamState> states,
        CancellationToken cancellationToken
    )
    {
        MockAuditStreamStore
            .Setup(x => x.GetDueStatesAsync(Now, AuditOptions.StreamsPerPoll, cancellationToken))
            .ReturnsAsync(states)
            .Verifiable();
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenAuditIsDisabled_PollsNothing()
    {
        var options = new AuditRecordOptions { Enabled = false };
        using var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    #endregion

    #region PollAsync Tests

    [Fact]
    public async Task PollAsync_Always_ClaimsEachDueStateBeforeDeliveringIt()
    {
        var cancellationToken = CancellationToken.None;
        var first = CreateState("stream-1");
        var second = CreateState("stream-2");
        SetupStores();
        SetupDue([first, second], cancellationToken);
        var events = new List<string>();
        MockAuditStreamStore
            .Setup(x =>
                x.UpdateStateAsync(It.IsAny<PersistedAuditStreamState>(), cancellationToken)
            )
            .Callback(
                (PersistedAuditStreamState state, CancellationToken _) =>
                    events.Add($"claim:{state.StreamId}")
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));
        MockDeliverer
            .Setup(x => x.DeliverAsync(It.IsAny<PersistedAuditStreamState>(), cancellationToken))
            .Callback(
                (PersistedAuditStreamState state, CancellationToken _) =>
                    events.Add($"deliver:{state.StreamId}")
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));

        await Service.PollAsync(cancellationToken);

        Assert.True(events.IndexOf("claim:stream-1") < events.IndexOf("deliver:stream-1"));
        Assert.True(events.IndexOf("claim:stream-2") < events.IndexOf("deliver:stream-2"));
        Assert.Equal(Service.LeaseHolder, first.LeaseHolder);
        Assert.Equal(Now + AuditOptions.StreamLeaseDuration, first.LeaseExpiryTime);
        Assert.Equal(Service.LeaseHolder, second.LeaseHolder);
        Assert.Equal(Now + AuditOptions.StreamLeaseDuration, second.LeaseExpiryTime);
    }

    [Fact]
    public async Task PollAsync_WhenAnotherReplicaClaimsAState_SkipsIt()
    {
        var cancellationToken = CancellationToken.None;
        var first = CreateState("stream-1");
        var second = CreateState("stream-2");
        SetupStores();
        SetupDue([first, second], cancellationToken);
        MockAuditStreamStore
            .Setup(x =>
                x.UpdateStateAsync(It.IsAny<PersistedAuditStreamState>(), cancellationToken)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));
        MockStoreManager.SetupSaveChangesFailingOnce(
            new StoreConcurrencyException("conflict"),
            Times.Exactly(2),
            cancellationToken
        );
        MockDeliverer
            .Setup(x => x.DeliverAsync(second, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Service.PollAsync(cancellationToken);
    }

    [Fact]
    public async Task PollAsync_WhenNothingIsDue_DeliversNothing()
    {
        var cancellationToken = CancellationToken.None;
        SetupStores();
        SetupDue([], cancellationToken);

        await Service.PollAsync(cancellationToken);
    }

    [Fact]
    public async Task PollAsync_WhenTheStoreFails_LogsAndReturns()
    {
        var cancellationToken = CancellationToken.None;
        SetupUnscoped();
        var failure = new InvalidOperationException("unreachable");
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(cancellationToken))
            .ThrowsAsync(failure)
            .Verifiable();

        await Service.PollAsync(cancellationToken);

        var logged = Assert.Single(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.AuditStreamPollFailed
        );
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Same(failure, logged.Exception);
    }

    #endregion
}
