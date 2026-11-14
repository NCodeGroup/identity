// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management.Transfers;

// See ADR-1514.
public sealed class LocalAccountTenantTransferContributorTests : IDisposable
{
    private const string TenantId = "tenant-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountTransferStore> MockTransferStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }

    public LocalAccountTenantTransferContributorTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTransferStore = MockRepository.Create<ILocalAccountTransferStore>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
    }

    public void Dispose() => MockRepository.Verify();

    private LocalAccountTenantTransferContributor CreateContributor() =>
        new(MockStoreManagerFactory.Object, MockAmbientTenantAccessor.Object);

    #region Helpers

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(CancellationToken.None))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountTransferStore>())
            .Returns(MockTransferStore.Object)
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

    private static LocalAccountTransferRecord CreateRecord(
        string localAccountId,
        string userName,
        string? passwordHash
    ) =>
        new()
        {
            LocalAccountId = localAccountId,
            UserName = userName,
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
    public async Task ExportAsync_WhenTheTenantHasAccounts_WritesTheStoreRecords()
    {
        SetupStoreManager();
        MockTransferStore
            .Setup(x => x.ExportAsync(CancellationToken.None))
            .ReturnsAsync([CreateRecord("account-1", "alice", "hash-1")])
            .Verifiable();

        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        Assert.Equal("local-accounts", section.Name);
        Assert.Equal(JsonValueKind.Array, section.Json.ValueKind);

        var record = Assert.Single(section.Json.EnumerateArray());
        Assert.Equal("account-1", record.GetProperty("localAccountId").GetString());
        Assert.Equal("alice", record.GetProperty("userName").GetString());
        Assert.Equal("hash-1", record.GetProperty("passwordHash").GetString());
        Assert.Equal("stamp-account-1", record.GetProperty("securityStamp").GetString());
        Assert.Equal("name", record.GetProperty("claims")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExportAsync_WhenTheTenantHasNoAccounts_WritesAnEmptyArray()
    {
        SetupStoreManager();
        MockTransferStore
            .Setup(x => x.ExportAsync(CancellationToken.None))
            .ReturnsAsync([])
            .Verifiable();

        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        Assert.Equal(JsonValueKind.Array, section.Json.ValueKind);
        Assert.Equal(0, section.Json.GetArrayLength());
    }

    #endregion

    #region ImportAsync Tests

    [Fact]
    public async Task ImportAsync_WhenTheSectionIsAnotherSection_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, CreateSection("[]", "core"), CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionIsNotAnArray_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, CreateSection("{}"), CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsRecords_WritesThemAndReturnsNoRemoteKeys()
    {
        SetupStoreManager();
        SetupSaveChanges();

        var exported = new[]
        {
            CreateRecord("account-1", "alice", "hash-1"),
            CreateRecord("account-2", "bob", null),
        };
        List<LocalAccountTransferRecord>? imported = null;
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    It.IsAny<IReadOnlyList<LocalAccountTransferRecord>>(),
                    CancellationToken.None
                )
            )
            .Callback<IReadOnlyList<LocalAccountTransferRecord>, CancellationToken>(
                (records, _) => imported = [.. records]
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var section = new TenantTransferSection
        {
            Name = "local-accounts",
            Json = JsonSerializer.SerializeToElement(
                exported,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ),
        };

        var result = await CreateContributor()
            .ImportAsync(TenantId, section, CancellationToken.None);

        Assert.Empty(result.RemoteKeys);
        Assert.NotNull(imported);
        Assert.Equal(2, imported.Count);

        var first = imported[0];
        Assert.Equal("account-1", first.LocalAccountId);
        Assert.Equal("ALICE", first.NormalizedUserName);
        Assert.Equal("hash-1", first.PasswordHash);
        Assert.Equal("stamp-account-1", first.SecurityStamp);
        Assert.Equal(exported[0].LockoutEnd, first.LockoutEnd);
        Assert.Equal(2, first.AccessFailedCount);
        var claim = Assert.Single(first.Claims);
        Assert.Equal("name", claim.Type);
        Assert.Equal("alice", claim.Value);

        var second = imported[1];
        Assert.Equal("account-2", second.LocalAccountId);
        Assert.Null(second.PasswordHash);
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsNoRecords_WritesNothingAndReturnsNoRemoteKeys()
    {
        SetupStoreManager();
        SetupSaveChanges();
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    It.Is<IReadOnlyList<LocalAccountTransferRecord>>(records => records.Count == 0),
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var result = await CreateContributor()
            .ImportAsync(TenantId, CreateSection("[]"), CancellationToken.None);

        Assert.Empty(result.RemoteKeys);
    }

    #endregion

    #region Round trip Tests

    [Fact]
    public async Task ImportAsync_AfterExportAsync_CarriesTheRecordsUnchanged()
    {
        var record = CreateRecord("account-1", "alice", "hash-1");

        SetupStoreManager();
        MockTransferStore
            .Setup(x => x.ExportAsync(CancellationToken.None))
            .ReturnsAsync([record])
            .Verifiable();
        var section = await CreateContributor().ExportAsync(TenantId, CancellationToken.None);

        MockStoreManagerFactory.Reset();
        MockStoreManager.Reset();
        MockAmbientTenantAccessor.Reset();
        SetupStoreManager();
        SetupSaveChanges();
        LocalAccountTransferRecord? imported = null;
        MockTransferStore
            .Setup(x =>
                x.ImportAsync(
                    It.IsAny<IReadOnlyList<LocalAccountTransferRecord>>(),
                    CancellationToken.None
                )
            )
            .Callback<IReadOnlyList<LocalAccountTransferRecord>, CancellationToken>(
                (records, _) => imported = Assert.Single(records)
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
    public async Task RemoveAsync_Always_RemovesEveryAccountOfTheTenantAndSaves()
    {
        SetupStoreManager();
        SetupSaveChanges();
        MockTransferStore
            .Setup(x => x.RemoveAllAsync(CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await CreateContributor().RemoveAsync(TenantId, CancellationToken.None);
    }

    #endregion
}
