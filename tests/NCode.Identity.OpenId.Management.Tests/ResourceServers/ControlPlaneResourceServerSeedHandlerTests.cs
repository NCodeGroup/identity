// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ResourceServers;

public sealed class ControlPlaneResourceServerSeedHandlerTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private const string TenantId = "tenant-1";
    private const string DirectoryIdentifier = GlobalDirectoryProtocol.DirectoryAudience;

    #region Helpers

    private static SeedTenantCommand CreateCommand(IStoreManager storeManager, TenantPlane plane)
    {
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            DisplayName = "Tenant One",
        }.Build();
        return new(
            new TenantSeedContext
            {
                Tenant = tenant,
                Plane = plane,
                StoreManager = storeManager,
            }
        );
    }

    private static PersistedResourceServer CreateExisting(string identifier) =>
        new PersistedResourceServerBuilder
        {
            TenantId = TenantId,
            ResourceServerId = "existing",
            Identifier = identifier,
            Name = "Existing",
            IsSystem = true,
            Settings = default,
        }.Build();

    private static ControlPlaneResourceServerSeedHandler CreateHandler(
        ICryptoService cryptoService
    ) => new(cryptoService);

    #endregion

    #region HandleAsync

    [Fact]
    public async Task HandleAsync_WhenRootPlaneAndAbsent_SeedsTheDirectoryResourceServer()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        foreach (var identifier in new[] { DirectoryIdentifier })
        {
            store
                .Setup(x =>
                    x.GetByIdentifierOrDefaultAsync(identifier, It.IsAny<CancellationToken>())
                )
                .ReturnsAsync((PersistedResourceServer?)null)
                .Verifiable();
        }
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("generated-rs-id")
            .Verifiable();

        var captured = new List<PersistedResourceServer>();
        store
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedResourceServer>(), It.IsAny<CancellationToken>())
            )
            .Callback((PersistedResourceServer rs, CancellationToken _) => captured.Add(rs))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var handler = CreateHandler(crypto.Object);
        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );

        Assert.Single(captured);

        var directory = Assert.Single(captured, rs => rs.Identifier == DirectoryIdentifier);
        Assert.True(directory.IsSystem);
        Assert.Equal(TenantId, directory.TenantId);
        var directoryScope = Assert.Single(directory.Scopes);
        Assert.Equal("directory", directoryScope.Value);
        Assert.True(directoryScope.IsSystem);
    }

    [Fact]
    public async Task HandleAsync_WhenRootPlaneAndAbsent_SeedsWithEmptyObjectSettings()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        foreach (var identifier in new[] { DirectoryIdentifier })
        {
            store
                .Setup(x =>
                    x.GetByIdentifierOrDefaultAsync(identifier, It.IsAny<CancellationToken>())
                )
                .ReturnsAsync((PersistedResourceServer?)null)
                .Verifiable();
        }
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("generated-rs-id")
            .Verifiable();

        var captured = new List<PersistedResourceServer>();
        store
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedResourceServer>(), It.IsAny<CancellationToken>())
            )
            .Callback((PersistedResourceServer rs, CancellationToken _) => captured.Add(rs))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var handler = CreateHandler(crypto.Object);
        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );

        Assert.Single(captured);
        Assert.All(
            captured,
            rs => Assert.True(JsonElement.DeepEquals(JsonElements.EmptyObject, rs.Settings.Value))
        );
    }

    [Fact]
    public async Task HandleAsync_WhenWorkloadPlane_DoesNothing()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var crypto = MockRepository.Create<ICryptoService>();

        var handler = CreateHandler(crypto.Object);

        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Workload),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task HandleAsync_WhenPresent_DoesNothing()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        foreach (var identifier in new[] { DirectoryIdentifier })
        {
            store
                .Setup(x =>
                    x.GetByIdentifierOrDefaultAsync(identifier, It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(CreateExisting(identifier))
                .Verifiable();
        }

        var handler = CreateHandler(crypto.Object);

        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );
    }

    #endregion

    #region MediatorPriority

    [Fact]
    public void MediatorPriority_Always_IsLow()
    {
        var crypto = MockRepository.Create<ICryptoService>();
        var handler = CreateHandler(crypto.Object);

        Assert.Equal(DefaultMediatorPriorities.Low, handler.MediatorPriority);
    }

    #endregion
}
