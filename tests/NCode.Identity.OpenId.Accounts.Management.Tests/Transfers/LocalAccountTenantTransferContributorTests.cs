// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management.Transfers;

// See ADR-1514.
public sealed class LocalAccountTenantTransferContributorTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string FirstConnectionId = "connection-1";
    private const string SecondConnectionId = "connection-2";
    private const string OtherStrategyConnectionId = "connection-3";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<ILocalAccountTransferStore> MockTransferStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private LocalAccountStoreCatalog Catalog { get; }

    public LocalAccountTenantTransferContributorTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockTransferStore = MockRepository.Create<ILocalAccountTransferStore>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();

        Catalog = new LocalAccountStoreCatalog([
            new LocalAccountStoreRegistration
            {
                Name = "default",
                CreateStore = _ => MockRepository.Create<ILocalAccountStore>().Object,
                CreateTransferStore = _ => MockTransferStore.Object,
            },
        ]);
    }

    public void Dispose() => MockRepository.Verify();

    private LocalAccountTenantTransferContributor CreateContributor() =>
        new(MockStoreManagerFactory.Object, MockAmbientTenantAccessor.Object, Catalog);

    #region Helpers

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(CancellationToken.None))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(scope.Object)
            .Verifiable();
    }

    private void SetupSaveChanges()
    {
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private void SetupAllConnections(params PersistedConnection[] connections)
    {
        MockConnectionStore
            .Setup(x => x.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(connections)
            .Verifiable();
    }

    private void SetupConnection(PersistedConnection connection)
    {
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync(connection.ConnectionId, CancellationToken.None))
            .ReturnsAsync(connection)
            .Verifiable();
    }

    private static PersistedConnection CreateConnection(
        string connectionId,
        string strategy = ConnectionStrategyNames.Local
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            Name = connectionId,
            Strategy = strategy,
            DisplayName = "Username and password",
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Value = JsonSerializer.SerializeToElement(new { }),
            },
        };

    private static LocalAccountTransferRecord CreateRecord(
        string localAccountId,
        string userName,
        string? passwordHash
    ) =>
        new()
        {
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            EmailVerified = true,
            PasswordHash = passwordHash,
            SecurityStamp = $"stamp-{localAccountId}",
            IsEnabled = true,
            LockoutEnabled = true,
            LockoutEnd = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            AccessFailedCount = 2,
            Claims = [new PersistedLocalAccountClaim { Type = "name", Value = userName }],
        };

    private static TenantTransferSection CreateSection(
        string json,
        string name = LocalAccountTenantTransferContributor.SectionName
    ) => new() { Name = name, Json = JsonElement.Parse(json) };

    #endregion

    #region Properties Tests

    [Fact]
    public void Name_Always_IsLocalAccounts()
    {
        Assert.Equal("local-accounts", CreateContributor().Name);
    }

    [Fact]
    public void Order_Always_Is100()
    {
        Assert.Equal(100, CreateContributor().Order);
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenTheTenantHasAccounts_WritesTheStoreRecordsByConnection()
    {
        SetupStoreManager();
        SetupAllConnections(
            CreateConnection(FirstConnectionId),
            CreateConnection(SecondConnectionId)
        );
        MockTransferStore
            .Setup(x => x.ExportAsync(FirstConnectionId, CancellationToken.None))
            .ReturnsAsync([CreateRecord("account-1", "alice", "hash-1")])
            .Verifiable();
        MockTransferStore
            .Setup(x => x.ExportAsync(SecondConnectionId, CancellationToken.None))
            .ReturnsAsync([CreateRecord("account-2", "bob", null)])
            .Verifiable();

        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        Assert.Equal("local-accounts", section.Name);
        Assert.Equal(JsonValueKind.Object, section.Json.ValueKind);
        Assert.Equal(2, section.Json.EnumerateObject().Count());

        var first = Assert.Single(section.Json.GetProperty(FirstConnectionId).EnumerateArray());
        Assert.Equal("account-1", first.GetProperty("localAccountId").GetString());
        Assert.Equal("alice", first.GetProperty("userName").GetString());
        Assert.Equal("hash-1", first.GetProperty("passwordHash").GetString());
        Assert.Equal("stamp-account-1", first.GetProperty("securityStamp").GetString());
        Assert.Equal("name", first.GetProperty("claims")[0].GetProperty("type").GetString());

        var second = Assert.Single(section.Json.GetProperty(SecondConnectionId).EnumerateArray());
        Assert.Equal("account-2", second.GetProperty("localAccountId").GetString());
    }

    [Fact]
    public async Task ExportAsync_WhenAConnectionIsNotLocal_LeavesItOut()
    {
        SetupStoreManager();
        SetupAllConnections(
            CreateConnection(FirstConnectionId),
            CreateConnection(OtherStrategyConnectionId, strategy: "other")
        );
        MockTransferStore
            .Setup(x => x.ExportAsync(FirstConnectionId, CancellationToken.None))
            .ReturnsAsync([])
            .Verifiable();

        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        var property = Assert.Single(section.Json.EnumerateObject());
        Assert.Equal(FirstConnectionId, property.Name);
        Assert.Equal(0, property.Value.GetArrayLength());
    }

    [Fact]
    public async Task ExportAsync_WhenTheTenantHasNoConnections_WritesAnEmptyObject()
    {
        SetupStoreManager();
        SetupAllConnections();

        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        Assert.Equal(JsonValueKind.Object, section.Json.ValueKind);
        Assert.Empty(section.Json.EnumerateObject());
    }

    #endregion

    #region ImportAsync Tests

    [Fact]
    public async Task ImportAsync_WhenTheSectionIsAnotherSection_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, CreateSection("{}", "core"), CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionIsNotAnObject_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, CreateSection("[]"), CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionNamesAnUnknownConnection_Throws()
    {
        SetupStoreManager();
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync("unknown", CancellationToken.None))
            .ReturnsAsync((PersistedConnection?)null)
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, CreateSection("""{"unknown":[]}"""), CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsRecords_WritesThemPerConnectionAndReturnsNoRemoteKeys()
    {
        SetupStoreManager();
        SetupSaveChanges();
        SetupConnection(CreateConnection(FirstConnectionId));
        SetupConnection(CreateConnection(SecondConnectionId));

        var exported = new[]
        {
            CreateRecord("account-1", "alice", "hash-1"),
            CreateRecord("account-2", "bob", null),
        };
        List<LocalAccountTransferRecord>? firstImported = null;
        List<LocalAccountTransferRecord>? secondImported = null;
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    FirstConnectionId,
                    It.IsAny<IReadOnlyList<LocalAccountTransferRecord>>(),
                    CancellationToken.None
                )
            )
            .Callback<string, IReadOnlyList<LocalAccountTransferRecord>, CancellationToken>(
                (_, records, _) => firstImported = [.. records]
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    SecondConnectionId,
                    It.IsAny<IReadOnlyList<LocalAccountTransferRecord>>(),
                    CancellationToken.None
                )
            )
            .Callback<string, IReadOnlyList<LocalAccountTransferRecord>, CancellationToken>(
                (_, records, _) => secondImported = [.. records]
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var section = new TenantTransferSection
        {
            Name = "local-accounts",
            Json = JsonSerializer.SerializeToElement(
                new Dictionary<string, LocalAccountTransferRecord[]>
                {
                    [FirstConnectionId] = [exported[0]],
                    [SecondConnectionId] = [exported[1]],
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ),
        };

        var result = await CreateContributor()
            .ImportAsync(TenantId, section, CancellationToken.None);

        Assert.Empty(result.RemoteKeys);

        Assert.NotNull(firstImported);
        var first = Assert.Single(firstImported);
        Assert.Equal("account-1", first.LocalAccountId);
        Assert.Equal("ALICE", first.NormalizedUserName);
        Assert.Equal("hash-1", first.PasswordHash);
        Assert.Equal("stamp-account-1", first.SecurityStamp);
        Assert.Equal(exported[0].LockoutEnd, first.LockoutEnd);
        Assert.Equal(2, first.AccessFailedCount);
        var claim = Assert.Single(first.Claims);
        Assert.Equal("name", claim.Type);
        Assert.Equal("alice", claim.Value);

        Assert.NotNull(secondImported);
        var second = Assert.Single(secondImported);
        Assert.Equal("account-2", second.LocalAccountId);
        Assert.Null(second.PasswordHash);
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsNoConnections_WritesNothingAndReturnsNoRemoteKeys()
    {
        SetupStoreManager();
        SetupSaveChanges();

        var result = await CreateContributor()
            .ImportAsync(TenantId, CreateSection("{}"), CancellationToken.None);

        Assert.Empty(result.RemoteKeys);
    }

    #endregion

    #region Round trip Tests

    [Fact]
    public async Task ImportAsync_AfterExportAsync_CarriesTheRecordsUnchanged()
    {
        var record = CreateRecord("account-1", "alice", "hash-1");
        var connection = CreateConnection(FirstConnectionId);

        SetupStoreManager();
        SetupAllConnections(connection);
        MockTransferStore
            .Setup(x => x.ExportAsync(FirstConnectionId, CancellationToken.None))
            .ReturnsAsync([record])
            .Verifiable();
        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        MockStoreManagerFactory.Reset();
        MockStoreManager.Reset();
        MockConnectionStore.Reset();
        MockTransferStore.Reset();
        MockAmbientTenantAccessor.Reset();
        SetupStoreManager();
        SetupSaveChanges();
        SetupConnection(connection);
        LocalAccountTransferRecord? imported = null;
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    FirstConnectionId,
                    It.IsAny<IReadOnlyList<LocalAccountTransferRecord>>(),
                    CancellationToken.None
                )
            )
            .Callback<string, IReadOnlyList<LocalAccountTransferRecord>, CancellationToken>(
                (_, records, _) => imported = Assert.Single(records)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None);

        Assert.NotNull(imported);
        Assert.Equal(record.LocalAccountId, imported.LocalAccountId);
        Assert.Equal(record.UserName, imported.UserName);
        Assert.Equal(record.NormalizedUserName, imported.NormalizedUserName);
        Assert.Equal(record.Email, imported.Email);
        Assert.Equal(record.NormalizedEmail, imported.NormalizedEmail);
        Assert.Equal(record.EmailVerified, imported.EmailVerified);
        Assert.Equal(record.PasswordHash, imported.PasswordHash);
        Assert.Equal(record.SecurityStamp, imported.SecurityStamp);
        Assert.Equal(record.IsEnabled, imported.IsEnabled);
        Assert.Equal(record.LockoutEnabled, imported.LockoutEnabled);
        Assert.Equal(record.LockoutEnd, imported.LockoutEnd);
        Assert.Equal(record.AccessFailedCount, imported.AccessFailedCount);
        Assert.Equal(record.Claims[0].Type, imported.Claims[0].Type);
        Assert.Equal(record.Claims[0].Value, imported.Claims[0].Value);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_Always_RemovesEveryAccountOfEveryLocalConnectionAndSaves()
    {
        SetupStoreManager();
        SetupSaveChanges();
        SetupAllConnections(
            CreateConnection(FirstConnectionId),
            CreateConnection(SecondConnectionId),
            CreateConnection(OtherStrategyConnectionId, strategy: "other")
        );
        MockTransferStore
            .Setup(x => x.RemoveAllAsync(FirstConnectionId, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockTransferStore
            .Setup(x => x.RemoveAllAsync(SecondConnectionId, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await CreateContributor().RemoveAsync(TenantId, CancellationToken.None);
    }

    #endregion
}
