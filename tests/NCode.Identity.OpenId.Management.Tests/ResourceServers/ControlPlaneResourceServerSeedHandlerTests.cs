// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
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
    private const string DirectoryIdentifier = "urn:ncode:directory";
    private const string ControlPlaneIdentifier = "urn:ncode:control-plane";

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
        ICryptoService cryptoService,
        bool isControlPlane
    ) => new(cryptoService, Options.Create(new CellOptions { IsControlPlane = isControlPlane }));

    #endregion

    #region HandleAsync

    [Fact]
    public async Task HandleAsync_WhenControlPlaneRootAndAbsent_SeedsBothResourceServers()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        foreach (var identifier in new[] { DirectoryIdentifier, ControlPlaneIdentifier })
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

        var handler = CreateHandler(crypto.Object, isControlPlane: true);
        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );

        Assert.Equal(2, captured.Count);

        var directory = Assert.Single(captured, rs => rs.Identifier == DirectoryIdentifier);
        Assert.True(directory.IsSystem);
        Assert.Equal(TenantId, directory.TenantId);
        var directoryScope = Assert.Single(directory.Scopes);
        Assert.Equal("directory", directoryScope.Value);
        Assert.True(directoryScope.IsSystem);

        var controlPlane = Assert.Single(captured, rs => rs.Identifier == ControlPlaneIdentifier);
        Assert.True(controlPlane.IsSystem);
        var controlPlaneScope = Assert.Single(controlPlane.Scopes);
        Assert.Equal("control-plane", controlPlaneScope.Value);
        Assert.True(controlPlaneScope.IsSystem);
    }

    [Fact]
    public async Task HandleAsync_WhenWorkloadPlane_DoesNothing()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var crypto = MockRepository.Create<ICryptoService>();

        var handler = CreateHandler(crypto.Object, isControlPlane: true);

        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Workload),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task HandleAsync_WhenNotControlPlaneCell_DoesNothing()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var crypto = MockRepository.Create<ICryptoService>();

        var handler = CreateHandler(crypto.Object, isControlPlane: false);

        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task HandleAsync_WhenBothPresent_DoesNothing()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        foreach (var identifier in new[] { DirectoryIdentifier, ControlPlaneIdentifier })
        {
            store
                .Setup(x =>
                    x.GetByIdentifierOrDefaultAsync(identifier, It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(CreateExisting(identifier))
                .Verifiable();
        }

        var handler = CreateHandler(crypto.Object, isControlPlane: true);

        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyDirectoryPresent_SeedsControlPlaneOnly()
    {
        var manager = MockRepository.Create<IStoreManager>();
        var store = MockRepository.Create<IResourceServerStore>();
        var crypto = MockRepository.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IResourceServerStore>()).Returns(store.Object).Verifiable();
        store
            .Setup(x =>
                x.GetByIdentifierOrDefaultAsync(DirectoryIdentifier, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateExisting(DirectoryIdentifier))
            .Verifiable();
        store
            .Setup(x =>
                x.GetByIdentifierOrDefaultAsync(
                    ControlPlaneIdentifier,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((PersistedResourceServer?)null)
            .Verifiable();
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("generated-rs-id")
            .Verifiable();

        PersistedResourceServer? captured = null;
        store
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedResourceServer>(), It.IsAny<CancellationToken>())
            )
            .Callback((PersistedResourceServer rs, CancellationToken _) => captured = rs)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var handler = CreateHandler(crypto.Object, isControlPlane: true);
        await handler.HandleAsync(
            CreateCommand(manager.Object, TenantPlane.Root),
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal(ControlPlaneIdentifier, captured.Identifier);
    }

    #endregion

    #region MediatorPriority

    [Fact]
    public void MediatorPriority_Always_IsLow()
    {
        var crypto = MockRepository.Create<ICryptoService>();
        var handler = CreateHandler(crypto.Object, isControlPlane: true);

        Assert.Equal(DefaultMediatorPriorities.Low, handler.MediatorPriority);
    }

    #endregion
}
