// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultDirectoryOutboxTests : BaseTests
{
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IDirectoryOutboxStore> MockStore { get; }
    private DefaultDirectoryOutbox Outbox { get; }

    public DefaultDirectoryOutboxTests()
    {
        var mockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockStore = CreateStrictMock<IDirectoryOutboxStore>();
        var mockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();

        mockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(DefaultDirectoryOutbox.UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object);
        mockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<IDirectoryOutboxStore>()).Returns(MockStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        Outbox = new DefaultDirectoryOutbox(
            mockStoreManagerFactory.Object,
            mockAmbientTenantAccessor.Object
        );
    }

    [Fact]
    public async Task ListPendingAsync_MapsEntriesToDocuments()
    {
        MockStore
            .Setup(x => x.ListPendingAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PersistedDirectoryOutboxEntry
                {
                    Sequence = 1,
                    Kind = ReplicatedDocumentKinds.PlatformAuthority,
                    Key = "",
                    Revision = 3,
                    ContentJson = "{\"assignments\":[]}",
                    CreatedWhen = DateTimeOffset.UnixEpoch,
                },
                new PersistedDirectoryOutboxEntry
                {
                    Sequence = 2,
                    Kind = ReplicatedDocumentKinds.TeamAuthority,
                    Key = "t1",
                    Revision = 2,
                    ContentJson = null,
                    CreatedWhen = DateTimeOffset.UnixEpoch,
                },
            ]);

        var actual = await Outbox.ListPendingAsync(10, CancellationToken.None);

        Assert.Equal(2, actual.Count);
        Assert.Equal(3, actual[0].Revision);
        Assert.Equal(JsonValueKind.Object, actual[0].Content!.Value.ValueKind);
        Assert.Null(actual[1].Content);
    }

    [Fact]
    public async Task AcknowledgeAsync_RemovesThroughTheRevisionAndSaves()
    {
        MockStore
            .Setup(x =>
                x.RemoveThroughAsync("management-keys", "", 4, It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Outbox.AcknowledgeAsync("management-keys", "", 4, CancellationToken.None);

        MockStore.Verify();
        MockStoreManager.Verify();
    }

    [Fact]
    public async Task ListLocalRevisionsAsync_ReturnsDocumentsWithoutContent()
    {
        MockStore
            .Setup(x => x.ListSourceRevisionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PersistedSourceRevision
                {
                    Kind = ReplicatedDocumentKinds.TeamAuthority,
                    Key = "t1",
                    Revision = 6,
                },
            ]);

        var actual = await Outbox.ListLocalRevisionsAsync(CancellationToken.None);

        var document = Assert.Single(actual);
        Assert.Equal("t1", document.Key);
        Assert.Equal(6, document.Revision);
        Assert.Null(document.Content);
    }

    [Fact]
    public async Task RepublishAsync_EnqueuesTheCurrentRowsAndSaves()
    {
        MockStore
            .Setup(x =>
                x.EnqueueCurrentAsync("team-authority", "t1", 9, It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Outbox.RepublishAsync("team-authority", "t1", 9, CancellationToken.None);

        MockStore.Verify();
        MockStoreManager.Verify();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Policy_IsPublishingOnlyInTheControlPlaneCellOfAPlatformWithADirectory(
        bool hasDirectory,
        bool isControlPlane,
        bool expected
    )
    {
        var mockTopology = CreateStrictMock<IPlatformTopology>();
        mockTopology.Setup(x => x.HasDirectory).Returns(hasDirectory);
        mockTopology.Setup(x => x.IsControlPlaneCell).Returns(isControlPlane);
        var mockResolver = CreateStrictMock<ITenantResolver>();
        mockResolver.Setup(x => x.GetControlPlaneTenantId()).Returns("root");
        var signal = new DefaultDirectoryOutboxSignal();

        var policy = new DefaultDirectoryOutboxPolicy(
            mockTopology.Object,
            mockResolver.Object,
            signal
        );

        Assert.Equal(expected, policy.IsPublishing);
        Assert.Equal("root", policy.ControlPlaneTenantId);
    }

    [Fact]
    public async Task Policy_Notify_WakesTheSignal()
    {
        var signal = new DefaultDirectoryOutboxSignal();
        var policy = new DefaultDirectoryOutboxPolicy(
            CreateStrictMock<IPlatformTopology>().Object,
            CreateStrictMock<ITenantResolver>().Object,
            signal
        );

        policy.Notify();
        policy.Notify();

        await signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }
}
