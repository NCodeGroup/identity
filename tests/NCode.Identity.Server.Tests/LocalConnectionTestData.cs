// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;

namespace NCode.Identity.Server.Tests;

/// <summary>
/// Builds the <c>local</c> connection, its store catalog and its connection store that the seed tests share.
/// </summary>
internal static class LocalConnectionTestData
{
    public const string ConnectionId = "local-connection-id";

    public static PersistedConnection CreateConnection(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ConnectionId = ConnectionId,
            Name = ConnectionDefaults.Name,
            Strategy = ConnectionStrategyNames.Local,
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(
                new LocalConnectionOptions { Store = LocalAccountStoreNames.Default }
            ),
            Settings = new PersistedConnectionSettings
            {
                TenantId = tenantId,
                ConnectionId = ConnectionId,
                Value = JsonElements.EmptyObject,
            },
        };

    public static LocalAccountStoreCatalog CreateCatalog(ILocalAccountStore? store = null)
    {
        var registration = new LocalAccountStoreRegistration
        {
            Name = LocalAccountStoreNames.Default,
            CreateStore = _ => store ?? new Mock<ILocalAccountStore>(MockBehavior.Strict).Object,
            CreateTransferStore = _ =>
                new Mock<ILocalAccountTransferStore>(MockBehavior.Strict).Object,
        };
        return new LocalAccountStoreCatalog([registration]);
    }

    public static IConnectionStore CreateConnectionStore(PersistedConnection? connection)
    {
        var mockStore = new Mock<IConnectionStore>(MockBehavior.Strict);
        mockStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(ConnectionDefaults.Name, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(connection)
            .Verifiable();
        return mockStore.Object;
    }
}
