// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Stores;

public sealed class LocalAccountStoreCatalogTests
{
    private sealed class FakeStore : ILocalAccountStore
    {
        public TStore GetStore<TStore>()
            where TStore : IStore => throw new NotSupportedException();

        public object? GetService(Type serviceType) => throw new NotSupportedException();

        public ValueTask<PersistedLocalAccount?> GetByIdOrDefaultAsync(
            string localAccountId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PersistedLocalAccount?> GetByUserNameOrDefaultAsync(
            string connectionId,
            string userName,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PersistedLocalAccount?> GetByEmailOrDefaultAsync(
            string connectionId,
            string email,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PagedResult<PersistedLocalAccount>> GetPageAsync(
            string connectionId,
            PageRequest page,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask AddAsync(
            PersistedLocalAccount account,
            ReadOnlyMemory<byte>? password,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask UpdateAsync(
            PersistedLocalAccount account,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask RemoveAsync(string localAccountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask ReplaceClaimsAsync(
            string localAccountId,
            IReadOnlyList<PersistedLocalAccountClaim> claims,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask SetPasswordAsync(
            string localAccountId,
            ReadOnlyMemory<byte> password,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<LocalAccountVerificationResult> VerifyCredentialAsync(
            LocalAccountSignInIdentifier identifier,
            ReadOnlyMemory<byte> password,
            Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class FakeTransferStore : ILocalAccountTransferStore
    {
        public TStore GetStore<TStore>()
            where TStore : IStore => throw new NotSupportedException();

        public object? GetService(Type serviceType) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<LocalAccountTransferRecord>> ExportAsync(
            string connectionId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask ImportAsync(
            string connectionId,
            IReadOnlyList<LocalAccountTransferRecord> records,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask RemoveAllAsync(string connectionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStoreManager : IStoreManager
    {
        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public TStore GetStore<TStore>()
            where TStore : IStore => throw new NotSupportedException();

        public object? GetService(Type serviceType) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static LocalAccountStoreRegistration CreateRegistration(
        string name,
        ILocalAccountStore store,
        ILocalAccountTransferStore transferStore
    ) =>
        new()
        {
            Name = name,
            CreateStore = _ => store,
            CreateTransferStore = _ => transferStore,
        };

    private static PersistedConnection CreateConnection(string strategy, string optionsJson) =>
        new()
        {
            TenantId = "tenant",
            ConnectionId = "connection-id",
            Name = "local",
            Strategy = strategy,
            DisplayName = "Local",
            IsSystem = false,
            IsEnabled = true,
            Options = JsonSerializer.Deserialize<JsonElement>(optionsJson),
            Settings = new PersistedConnectionSettings
            {
                TenantId = "tenant",
                ConnectionId = "connection-id",
                Value = JsonSerializer.Deserialize<JsonElement>("{}"),
            },
        };

    private static LocalAccountStoreCatalog CreateCatalog() =>
        new([CreateRegistration("default", new FakeStore(), new FakeTransferStore())]);

    #region Contains Tests

    [Fact]
    public void Contains_WhenRegistered_ReturnsTrue()
    {
        var catalog = CreateCatalog();

        Assert.True(catalog.Contains("default"));
        Assert.False(catalog.Contains("other"));
    }

    [Fact]
    public void Constructor_WhenDuplicateName_Throws()
    {
        var registrations = new[]
        {
            CreateRegistration("default", new FakeStore(), new FakeTransferStore()),
            CreateRegistration("default", new FakeStore(), new FakeTransferStore()),
        };

        Assert.Throws<InvalidOperationException>(() => new LocalAccountStoreCatalog(registrations));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WhenNameIsNullOrEmpty_Throws(string? name)
    {
        var registrations = new[]
        {
            CreateRegistration(name!, new FakeStore(), new FakeTransferStore()),
        };

        Assert.Throws<InvalidOperationException>(() => new LocalAccountStoreCatalog(registrations));
    }

    #endregion

    #region GetStore Tests

    [Fact]
    public void GetStore_WhenConnectionNamesRegisteredStore_ReturnsItsStores()
    {
        var store = new FakeStore();
        var transferStore = new FakeTransferStore();
        var catalog = new LocalAccountStoreCatalog([
            CreateRegistration("default", store, transferStore),
        ]);
        var connection = CreateConnection("local", """{"store":"default"}""");

        Assert.Same(store, catalog.GetStore(new FakeStoreManager(), connection));
        Assert.Same(transferStore, catalog.GetTransferStore(new FakeStoreManager(), connection));
    }

    [Fact]
    public void GetStore_WhenStrategyIsNotLocal_Throws()
    {
        var catalog = CreateCatalog();
        var connection = CreateConnection("other", """{"store":"default"}""");

        Assert.Throws<InvalidOperationException>(() =>
            catalog.GetStore(new FakeStoreManager(), connection)
        );
    }

    [Fact]
    public void GetTransferStore_WhenStoreIsNotRegistered_Throws()
    {
        var catalog = CreateCatalog();
        var connection = CreateConnection("local", """{"store":"missing"}""");

        Assert.Throws<InvalidOperationException>(() =>
            catalog.GetTransferStore(new FakeStoreManager(), connection)
        );
    }

    #endregion
}
