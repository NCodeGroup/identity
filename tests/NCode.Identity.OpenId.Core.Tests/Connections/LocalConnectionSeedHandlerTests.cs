// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public sealed class LocalConnectionSeedHandlerTests
{
    private const string TenantId = "tenant-1";

    private static SeedTenantCommand CreateCommand(IStoreManager storeManager)
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
                Plane = TenantPlane.Workload,
                StoreManager = storeManager,
            }
        );
    }

    [Fact]
    public async Task HandleAsync_WhenAbsent_AddsLocalConnection()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var manager = mocks.Create<IStoreManager>();
        var store = mocks.Create<IConnectionStore>();
        var crypto = mocks.Create<ICryptoService>();

        manager.Setup(x => x.GetStore<IConnectionStore>()).Returns(store.Object).Verifiable();
        store
            .Setup(x => x.GetByNameOrDefaultAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedConnection?)null)
            .Verifiable();
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("generated-connection-id")
            .Verifiable();

        PersistedConnection? captured = null;
        store
            .Setup(x => x.AddAsync(It.IsAny<PersistedConnection>(), It.IsAny<CancellationToken>()))
            .Callback((PersistedConnection c, CancellationToken _) => captured = c)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var handler = new LocalConnectionSeedHandler(crypto.Object);

        await handler.HandleAsync(CreateCommand(manager.Object), CancellationToken.None);

        mocks.Verify();
        Assert.NotNull(captured);
        Assert.Equal(TenantId, captured.TenantId);
        Assert.Equal("generated-connection-id", captured.ConnectionId);
        Assert.Equal("local", captured.Name);
        Assert.Equal("local", captured.Strategy);
        Assert.Equal("Username and password", captured.DisplayName);
        Assert.True(captured.IsSystem);
        Assert.True(captured.IsEnabled);
        Assert.Equal("""{"store":"default"}""", captured.Options.GetRawText());
        Assert.Equal(TenantId, captured.Settings.TenantId);
        Assert.Equal("generated-connection-id", captured.Settings.ConnectionId);
        Assert.True(JsonElement.DeepEquals(JsonElements.EmptyObject, captured.Settings.Value));
    }

    [Fact]
    public async Task HandleAsync_WhenPresent_DoesNothing()
    {
        var mocks = new MockRepository(MockBehavior.Strict);
        var manager = mocks.Create<IStoreManager>();
        var store = mocks.Create<IConnectionStore>();
        var crypto = mocks.Create<ICryptoService>();

        var existing = new PersistedConnection
        {
            TenantId = TenantId,
            ConnectionId = "existing",
            Name = "local",
            Strategy = "local",
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = true,
            Options = JsonElements.EmptyObject,
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = "existing",
                Value = JsonElements.EmptyObject,
            },
        };

        manager.Setup(x => x.GetStore<IConnectionStore>()).Returns(store.Object).Verifiable();
        store
            .Setup(x => x.GetByNameOrDefaultAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing)
            .Verifiable();

        var handler = new LocalConnectionSeedHandler(crypto.Object);

        await handler.HandleAsync(CreateCommand(manager.Object), CancellationToken.None);

        mocks.Verify();
    }
}
