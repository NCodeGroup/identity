// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultReplicatedDocumentApplierTests : BaseTests
{
    private const string UnscopedReason = "directory replicated document";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IReplicatedDocumentStore> MockStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private FakeTimeProvider TimeProvider { get; } = new();
    private DefaultReplicatedDocumentApplier Applier { get; }

    public DefaultReplicatedDocumentApplierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockStore = CreateStrictMock<IReplicatedDocumentStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();

        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object)
            .Verifiable();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IReplicatedDocumentStore>())
            .Returns(MockStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        Applier = new DefaultReplicatedDocumentApplier(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            TimeProvider
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentIsNewer_StoresItAndSaves()
    {
        MockStore
            .Setup(x =>
                x.TryApplyAsync(
                    It.Is<PersistedReplicatedDocument>(document =>
                        document.Kind == ReplicatedDocumentKinds.TeamAuthority
                        && document.Key == "team-a"
                        && document.Revision == 3
                        && document.ContentJson == """{"assignments":[]}"""
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Applier.ApplyAsync(
            new ReplicatedDocument
            {
                Kind = ReplicatedDocumentKinds.TeamAuthority,
                Key = "team-a",
                Revision = 3,
                Content = JsonElement.Parse("""{"assignments":[]}"""),
            },
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRevisionIsLower_ChangesNothing()
    {
        MockStore
            .Setup(x =>
                x.TryApplyAsync(
                    It.IsAny<PersistedReplicatedDocument>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();

        // Strict mocks: neither a revoked key nor a save is set up, so either would fail the test.
        await Applier.ApplyAsync(
            new ReplicatedDocument
            {
                Kind = ReplicatedDocumentKinds.ManagementKeys,
                Revision = 1,
                Content = JsonElement.Parse("""{"keys":[],"revokedKeyIds":["k1"]}"""),
            },
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenAManagementKeysDocumentRevokesKeys_RecordsEachRevokedKey()
    {
        MockStore
            .Setup(x =>
                x.TryApplyAsync(
                    It.IsAny<PersistedReplicatedDocument>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        foreach (var keyId in new[] { "k1", "k2" })
        {
            MockStore
                .Setup(x =>
                    x.AddRevokedKeyAsync(
                        It.Is<PersistedRevokedManagementKey>(key =>
                            key.KeyId == keyId
                            && key.Source == PersistedRevokedManagementKey.ControlPlaneSource
                            && key.RevokedWhen == TimeProvider.GetUtcNow()
                        ),
                        It.IsAny<CancellationToken>()
                    )
                )
                .Returns(ValueTask.CompletedTask)
                .Verifiable();
        }
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Applier.ApplyAsync(
            new ReplicatedDocument
            {
                Kind = ReplicatedDocumentKinds.ManagementKeys,
                Revision = 2,
                Content = JsonElement.Parse("""{"keys":[],"revokedKeyIds":["k1","k2"]}"""),
            },
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentIsRetired_StoresItWithNullContent()
    {
        MockStore
            .Setup(x =>
                x.TryApplyAsync(
                    It.Is<PersistedReplicatedDocument>(document =>
                        document.Kind == ReplicatedDocumentKinds.TeamAuthority
                        && document.Revision == 4
                        && document.ContentJson == null
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Applier.ApplyAsync(
            new ReplicatedDocument
            {
                Kind = ReplicatedDocumentKinds.TeamAuthority,
                Key = "team-a",
                Revision = 4,
            },
            CancellationToken.None
        );
    }
}
