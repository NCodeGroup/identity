// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Tenants.Keys;

public sealed class TenantKeyCustodyReconcilerTests : BaseTests
{
    private const string CellName = "cell-1";
    private const string OtherCell = "cell-2";

    private Mock<IPlatformTopology> MockTopology { get; }
    private Mock<ITenantDirectory> MockTenantDirectory { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IExpiringKeySet> MockExpiringKeySet { get; }
    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private FakeLogger<TenantKeyCustodyReconciler> Logger { get; } = new();

    private TenantKeyCustodyReconciler Reconciler { get; }

    public TenantKeyCustodyReconcilerTests()
    {
        MockTopology = CreateStrictMock<IPlatformTopology>();
        MockTenantDirectory = CreateStrictMock<ITenantDirectory>();
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTenantStore = CreateStrictMock<ITenantStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockExpiringKeySet = CreateStrictMock<IExpiringKeySet>();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("tenant key custody reconciliation"))
            .Returns(Mock.Of<IDisposable>());

        Reconciler = new TenantKeyCustodyReconciler(
            MockTopology.Object,
            MockTenantDirectory.Object,
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockExpiringKeySet.Object,
            Options.Create(new CellOptions { Name = CellName }),
            TimeProvider,
            Logger
        );
    }

    private void SetupLease(bool acquired) =>
        MockExpiringKeySet
            .Setup(x =>
                x.TryAddAsync(
                    It.Is<string>(key =>
                        key.StartsWith(TenantKeyCustodyReconciler.LeasePurpose + ":")
                    ),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(acquired);

    private void SetupIncoming(params string[] tenantIds) =>
        MockTenantStore
            .Setup(x =>
                x.ListTenantIdsByKeyCustodyAsync(
                    TenantKeyCustody.Incoming,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(tenantIds);

    private void SetupPlacement(string tenantId, string? cell) =>
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                cell is null
                    ? null
                    : new TenantPlacement
                    {
                        TenantId = tenantId,
                        Cell = cell,
                        Region = "region-1",
                    }
            );

    private void SetupStoredCustody(string tenantId, string custody)
    {
        var tenant = new PersistedTenantBuilder { TenantId = tenantId }.Build();
        tenant.KeyCustody = custody;
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    [Fact]
    public async Task ReconcileAsync_WhenTenantIsNoLongerIncoming_LeavesItsCustody()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1");
        SetupPlacement("t1", CellName);
        SetupStoredCustody("t1", TenantKeyCustody.Outgoing);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockTenantStore.Verify(
            x =>
                x.SetKeyCustodyAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
        Assert.DoesNotContain(
            Logger.Collector.GetSnapshot(),
            record => record.Id.Id == EventIds.TenantKeyCustodyHeld
        );
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheSaveConflicts_LeavesTheTenantForTheNextPass()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1");
        SetupPlacement("t1", CellName);
        SetupStoredCustody("t1", TenantKeyCustody.Incoming);
        MockTenantStore
            .Setup(x =>
                x.SetKeyCustodyAsync(
                    "t1",
                    TenantKeyCustody.Held,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StoreConcurrencyException("conflict"));

        await Reconciler.ReconcileAsync(CancellationToken.None);

        var records = Logger.Collector.GetSnapshot();
        Assert.Contains(
            records,
            record =>
                record.Id.Id == EventIds.TenantKeyCustodyTenantFailed
                && record.Message.Contains("'t1'")
        );
        Assert.DoesNotContain(records, record => record.Id.Id == EventIds.TenantKeyCustodyHeld);
    }

    [Fact]
    public async Task ReconcileAsync_WhenDirectoryPlacesTenantHere_SetsHeld()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1");
        SetupPlacement("t1", CellName);
        SetupStoredCustody("t1", TenantKeyCustody.Incoming);
        MockTenantStore
            .Setup(x =>
                x.SetKeyCustodyAsync(
                    "t1",
                    TenantKeyCustody.Held,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockStoreManager.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_WhenDirectoryPlacesTenantElsewhereOrNowhere_LeavesIncoming()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1", "t2");
        SetupPlacement("t1", OtherCell);
        SetupPlacement("t2", null);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockTenantStore.Verify(
            x =>
                x.SetKeyCustodyAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task ReconcileAsync_WhenDirectoryUnavailable_SkipsThePass()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1", "t2");
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync("t1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException("down"));

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockTenantDirectory.Verify(
            x => x.GetPlacementOrDefaultAsync("t2", It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ReconcileAsync_WhenOneTenantFails_HoldsTheNextTenant()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(true);
        SetupIncoming("t1", "t2");
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync("t1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store error"));
        SetupPlacement("t2", CellName);
        SetupStoredCustody("t2", TenantKeyCustody.Incoming);
        MockTenantStore
            .Setup(x =>
                x.SetKeyCustodyAsync(
                    "t2",
                    TenantKeyCustody.Held,
                    null,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockTenantStore.Verify(
            x =>
                x.SetKeyCustodyAsync(
                    "t2",
                    TenantKeyCustody.Held,
                    null,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task ReconcileAsync_WithAConfiguredInterval_LeasesTheWindowOfThatLength()
    {
        var reconciler = new TenantKeyCustodyReconciler(
            MockTopology.Object,
            MockTenantDirectory.Object,
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockExpiringKeySet.Object,
            Options.Create(
                new CellOptions
                {
                    Name = CellName,
                    KeyCustodyReconcileInterval = TimeSpan.FromHours(1),
                }
            ),
            TimeProvider,
            Logger
        );
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        MockExpiringKeySet
            .Setup(x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false);

        await reconciler.ReconcileAsync(CancellationToken.None);

        MockExpiringKeySet.VerifyAll();
    }

    [Fact]
    public async Task ReconcileAsync_WhenAnotherReplicaHoldsTheLease_DoesNothing()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(true);
        SetupLease(false);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockStoreManagerFactory.Verify(
            x => x.CreateAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ReconcileAsync_WhenPlatformHasNoDirectory_DoesNothing()
    {
        MockTopology.SetupGet(x => x.HasDirectory).Returns(false);

        await Reconciler.ReconcileAsync(CancellationToken.None);

        MockExpiringKeySet.Verify(
            x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }
}
