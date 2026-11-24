// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultControlPlaneReplicaTests : BaseTests
{
    private const string UnscopedReason = "control-plane replica";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IReplicatedDocumentStore> MockStore { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<ISecretSerializer> MockSecretSerializer { get; }
    private FakeTimeProvider TimeProvider { get; } = new();
    private DefaultControlPlaneReplica Replica { get; }

    public DefaultControlPlaneReplicaTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockStore = CreateStrictMock<IReplicatedDocumentStore>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockSecretSerializer = CreateStrictMock<ISecretSerializer>();
        var mockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();

        mockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object)
            .Verifiable();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IReplicatedDocumentStore>())
            .Returns(MockStore.Object);
        MockStoreManager.Setup(x => x.GetStore<ITeamStore>()).Returns(MockTeamStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        TimeProvider.SetUtcNow(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Replica = new DefaultControlPlaneReplica(
            MockStoreManagerFactory.Object,
            mockAmbientTenantAccessor.Object,
            MockSecretSerializer.Object,
            TimeProvider
        );
    }

    private static PersistedReplicatedDocument Document(
        string kind,
        string key,
        string contentJson
    ) =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = 1,
            ContentJson = contentJson,
        };

    private void SetupDocument(string kind, string key, string? contentJson) =>
        MockStore
            .Setup(x => x.GetOrDefaultAsync(kind, key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentJson is null ? null : Document(kind, key, contentJson))
            .Verifiable();

    private void SetupRevoked(params string[] keyIds) =>
        MockStore
            .Setup(x => x.ListRevokedKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                keyIds
                    .Select(keyId => new PersistedRevokedManagementKey
                    {
                        KeyId = keyId,
                        RevokedWhen = TimeProvider.GetUtcNow(),
                        Source = PersistedRevokedManagementKey.ControlPlaneSource,
                    })
                    .ToList()
            )
            .Verifiable();

    private SecretKey SetupKey(string keyId)
    {
        var key = CreateLooseMock<SecretKey>();
        key.SetupGet(x => x.KeyId).Returns(keyId);
        MockSecretSerializer
            .Setup(x => x.DeserializePublicSecret(It.Is<PersistedSecret>(s => s.KeyId == keyId)))
            .Returns(key.Object)
            .Verifiable();
        return key.Object;
    }

    private static string KeyEntry(string keyId, string? expiresWhen = null) =>
        $$"""{"kid":"{{keyId}}","secretType":"rsa","algorithm":"PS256","use":"sig","keySizeBits":2048,"publicKey":"AAAA","expiresWhen":{{(expiresWhen is null ? "null" : $"\"{expiresWhen}\"")}} }""";

    [Fact]
    public async Task GetManagementKeysAsync_WhenNoDocumentIsHeld_HasNoKeys()
    {
        SetupDocument(ReplicatedDocumentKinds.ManagementKeys, "", null);
        SetupRevoked();

        var result = await Replica.GetManagementKeysAsync(CancellationToken.None);

        Assert.False(result.HasKeys);
        Assert.Empty(result.Keys);
    }

    [Fact]
    public async Task GetManagementKeysAsync_WhenAKeyIsRevoked_NeverReturnsIt()
    {
        var kept = SetupKey("k2");
        SetupDocument(
            ReplicatedDocumentKinds.ManagementKeys,
            "",
            $$"""{"keys":[{{KeyEntry("k1")}},{{KeyEntry("k2")}}],"revokedKeyIds":[]}"""
        );
        SetupRevoked("k1");

        var result = await Replica.GetManagementKeysAsync(CancellationToken.None);

        Assert.True(result.HasKeys);
        Assert.Same(kept, Assert.Single(result.Keys));
        Assert.Contains("k1", result.RevokedKeyIds);
    }

    [Fact]
    public async Task GetManagementKeysAsync_WhenAKeyIsExpired_SkipsIt()
    {
        var kept = SetupKey("k2");
        SetupDocument(
            ReplicatedDocumentKinds.ManagementKeys,
            "",
            $$"""{"keys":[{{KeyEntry("k1", "2020-01-01T00:00:00Z")}},{{KeyEntry("k2")}}],"revokedKeyIds":[]}"""
        );
        SetupRevoked();

        var result = await Replica.GetManagementKeysAsync(CancellationToken.None);

        Assert.Same(kept, Assert.Single(result.Keys));
    }

    [Fact]
    public async Task GetAuthorityAsync_WhenThePlatformDocumentListsTheClient_ReturnsItsClientAndRoles()
    {
        SetupDocument(
            ReplicatedDocumentKinds.PlatformAuthority,
            "",
            """{"assignments":[{"principalId":"p1","roleName":"GlobalAdmin"},{"principalId":"p2","roleName":"Other"}],"clients":[{"principalId":"p1","clientId":"console"}]}"""
        );

        var result = await Replica.GetAuthorityAsync("p1", null, CancellationToken.None);

        Assert.Equal(["GlobalAdmin"], result.PlatformRoles);
        Assert.Empty(result.TeamRoles);
        Assert.Equal("console", result.ClientId);
    }

    [Fact]
    public async Task GetAuthorityAsync_WhenATeamIsRequested_UnionsTheRolesOfTheTeamChainByLowerCasedKey()
    {
        SetupDocument(ReplicatedDocumentKinds.PlatformAuthority, "", null);
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync("Child", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedTeam
                {
                    TeamId = "Child",
                    AncestorTeamIds = ["Root", "Mid"],
                    ConcurrencyToken = string.Empty,
                    IsDisabled = false,
                    DisplayName = "Child",
                    Settings = null!,
                }
            )
            .Verifiable();
        SetupDocument(
            ReplicatedDocumentKinds.TeamAuthority,
            "root",
            """{"assignments":[{"principalId":"p1","roleName":"TeamAdmin"}]}"""
        );
        SetupDocument(ReplicatedDocumentKinds.TeamAuthority, "mid", null);
        SetupDocument(
            ReplicatedDocumentKinds.TeamAuthority,
            "child",
            """{"assignments":[{"principalId":"p1","roleName":"TeamAdmin"},{"principalId":"p1","roleName":"Auditor"}]}"""
        );

        var result = await Replica.GetAuthorityAsync("p1", "Child", CancellationToken.None);

        Assert.Equal(["Auditor", "TeamAdmin"], result.TeamRoles);
        Assert.Empty(result.PlatformRoles);
        Assert.Null(result.ClientId);
    }
}
