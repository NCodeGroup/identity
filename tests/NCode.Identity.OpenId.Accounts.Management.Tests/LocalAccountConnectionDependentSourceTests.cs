// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

// See ADR-1535.
public sealed class LocalAccountConnectionDependentSourceTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ConnectionId = "connection-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private LocalAccountConnectionDependentSource Source { get; }

    public LocalAccountConnectionDependentSourceTests()
    {
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockLocalAccountStore = MockRepository.Create<ILocalAccountStore>();

        var catalog = new LocalAccountStoreCatalog([
            new LocalAccountStoreRegistration
            {
                Name = "default",
                CreateStore = _ => MockLocalAccountStore.Object,
                CreateTransferStore = _ =>
                    MockRepository.Create<ILocalAccountTransferStore>().Object,
            },
        ]);
        Source = new LocalAccountConnectionDependentSource(catalog);
    }

    public void Dispose() => MockRepository.Verify();

    #region Helpers

    private static PersistedConnection CreateConnection(
        string strategy = ConnectionStrategyNames.Local
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = ConnectionId,
            Name = "local",
            Strategy = strategy,
            DisplayName = "Username and password",
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                Value = JsonSerializer.SerializeToElement(new { }),
            },
        };

    private static PersistedLocalAccount CreateAccount() =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = "account-1",
            ConnectionId = ConnectionId,
            DisplayName = "Alice",
            UserName = "alice",
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = "account-ct",
        };

    private void SetupPage(params PersistedLocalAccount[] accounts)
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetPageAsync(
                    ConnectionId,
                    It.Is<PageRequest>(page => page.Limit == 1),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedLocalAccount> { Items = accounts, NextCursor = null }
            )
            .Verifiable();
    }

    #endregion

    #region HasDependentsAsync Tests

    [Fact]
    public async Task HasDependentsAsync_WhenTheStoreHoldsAnAccountOfTheConnection_ReturnsTrue()
    {
        SetupPage(CreateAccount());

        var result = await Source.HasDependentsAsync(
            MockStoreManager.Object,
            CreateConnection(),
            CancellationToken.None
        );

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependentsAsync_WhenTheStoreHoldsNoAccountOfTheConnection_ReturnsFalse()
    {
        SetupPage();

        var result = await Source.HasDependentsAsync(
            MockStoreManager.Object,
            CreateConnection(),
            CancellationToken.None
        );

        Assert.False(result);
    }

    [Fact]
    public async Task HasDependentsAsync_WhenTheConnectionIsNotLocal_ReturnsFalseWithoutReadingAStore()
    {
        var result = await Source.HasDependentsAsync(
            MockStoreManager.Object,
            CreateConnection(strategy: "other"),
            CancellationToken.None
        );

        Assert.False(result);
    }

    #endregion
}
