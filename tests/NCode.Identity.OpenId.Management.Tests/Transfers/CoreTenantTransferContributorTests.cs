// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Transfers;

// See ADR-1514.
public sealed class CoreTenantTransferContributorTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ExportedPrincipalId = "exported-client-principal";
    private const string ImportedPrincipalId = "imported-client-principal";
    private const string UserPrincipalId = "user-principal";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IResourceServerStore> MockResourceServerStore { get; }
    private Mock<IClientStore> MockClientStore { get; }
    private Mock<IClientGrantStore> MockClientGrantStore { get; }
    private Mock<IRoleAssignmentStore> MockRoleAssignmentStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }

    private List<string> Calls { get; } = [];
    private List<PersistedTenant> AddedTenants { get; } = [];
    private List<PersistedConnection> AddedConnections { get; } = [];
    private List<PersistedResourceServer> AddedResourceServers { get; } = [];
    private List<PersistedClient> AddedClients { get; } = [];
    private List<PersistedClientGrant> AddedClientGrants { get; } = [];
    private List<PersistedRoleAssignment> AddedRoleAssignments { get; } = [];

    public CoreTenantTransferContributorTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockResourceServerStore = MockRepository.Create<IResourceServerStore>();
        MockClientStore = MockRepository.Create<IClientStore>();
        MockClientGrantStore = MockRepository.Create<IClientGrantStore>();
        MockRoleAssignmentStore = MockRepository.Create<IRoleAssignmentStore>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
    }

    public void Dispose() => MockRepository.Verify();

    private CoreTenantTransferContributor CreateContributor() =>
        new(MockStoreManagerFactory.Object, MockAmbientTenantAccessor.Object);

    #region Helpers

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(CancellationToken.None))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(scope.Object)
            .Verifiable();
    }

    private void SetupStores()
    {
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IResourceServerStore>())
            .Returns(MockResourceServerStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IClientStore>())
            .Returns(MockClientStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IClientGrantStore>())
            .Returns(MockClientGrantStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IRoleAssignmentStore>())
            .Returns(MockRoleAssignmentStore.Object)
            .Verifiable();
    }

    private static JsonElement Json(string json) => JsonElement.Parse(json);

    private static PersistedSecret CreateSecret(
        string secretId,
        string encodedValue,
        string secretEncoding = SecretEncodings.SymmetricRaw
    ) =>
        new PersistedSecretBuilder
        {
            SecretId = secretId,
            EncodedValue = encodedValue,
            SecretEncoding = secretEncoding,
        }.Build();

    private static PersistedTenant CreateTenant() =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            TeamId = "team-1",
            Issuer = "https://tenant-1.example.com",
            IsDisabled = true,
            DisplayName = "Tenant One",
            ConcurrencyToken = "tenant-concurrency-token",
            Settings = Json("""{"tenant-setting":1}"""),
            Secrets = [CreateSecret("tenant-secret", "tenant-envelope")],
        }.Build();

    private static PersistedConnection CreateConnection() =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = "connection-1",
            Name = "local",
            Strategy = "local",
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = true,
            ConcurrencyToken = "connection-concurrency-token",
            Options = Json("""{"store":"default"}"""),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = "connection-1",
                Value = Json("""{"connection-setting":4}"""),
            },
        };

    private static PersistedResourceServer CreateResourceServer() =>
        new PersistedResourceServerBuilder
        {
            TenantId = TenantId,
            ResourceServerId = "server-1",
            Identifier = "https://api.example.com",
            Name = "Server One",
            IsSystem = false,
            IsDisabled = false,
            Settings = Json("""{"server-setting":2}"""),
            Scopes =
            [
                new PersistedScope
                {
                    Value = "read",
                    Description = "Reads",
                    IsSystem = false,
                },
            ],
            Secrets =
            [
                CreateSecret("server-secret-1", "test-kms:key-1", SecretEncodings.Remote),
                CreateSecret("server-secret-2", "server-envelope"),
            ],
        }.Build();

    private static PersistedClient CreateClient() =>
        new PersistedClientBuilder
        {
            TenantId = TenantId,
            ClientId = "client-1",
            PrincipalId = ExportedPrincipalId,
            IsDisabled = true,
            ResourceServerId = "server-1",
            Settings = Json("""{"client-setting":3}"""),
            Secrets =
            [
                CreateSecret("client-secret-1", "test-kms:key-1", SecretEncodings.Remote),
                CreateSecret("client-secret-2", "test-kms:key-2", SecretEncodings.Remote),
                CreateSecret("client-secret-3", "not-a-reference", SecretEncodings.Remote),
                CreateSecret("client-secret-4", "test-kms:key-3"),
            ],
        }.Build();

    private static PersistedClientGrant CreateClientGrant() =>
        new()
        {
            TenantId = TenantId,
            ClientId = "client-1",
            ResourceServerId = "server-1",
            ConcurrencyToken = "grant-concurrency-token",
            Scopes = ["read", "write"],
        };

    private static PersistedRoleAssignment CreateRoleAssignment(
        string assignmentId,
        string principalId
    ) =>
        new()
        {
            TenantId = TenantId,
            AssignmentId = assignmentId,
            PrincipalId = principalId,
            RoleName = "owner",
            ResourceType = ResourceNodeTypes.Tenant,
            ResourceId = TenantId,
            ConcurrencyToken = "assignment-concurrency-token",
        };

    private void SetupSource()
    {
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, CancellationToken.None))
            .ReturnsAsync(CreateTenant())
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.GetAllAsync(CancellationToken.None))
            .ReturnsAsync([CreateConnection()])
            .Verifiable();
        MockResourceServerStore
            .Setup(x =>
                x.GetPageAsync(
                    It.Is<PageRequest>(request => request.Cursor == null && request.Limit == 100),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedResourceServer>
                {
                    Items = [CreateResourceServer()],
                    NextCursor = null,
                }
            )
            .Verifiable();
        MockClientStore
            .Setup(x =>
                x.GetPageAsync(
                    It.Is<PageRequest>(request => request.Cursor == null && request.Limit == 100),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedClient> { Items = [CreateClient()], NextCursor = null }
            )
            .Verifiable();
        MockClientGrantStore
            .Setup(x =>
                x.GetPageAsync(
                    "client-1",
                    It.Is<PageRequest>(request => request.Cursor == null && request.Limit == 100),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedClientGrant>
                {
                    Items = [CreateClientGrant()],
                    NextCursor = null,
                }
            )
            .Verifiable();
        MockRoleAssignmentStore
            .Setup(x =>
                x.GetByResourceAsync(ResourceNodeTypes.Tenant, TenantId, CancellationToken.None)
            )
            .ReturnsAsync([
                CreateRoleAssignment("assignment-1", UserPrincipalId),
                CreateRoleAssignment("assignment-2", ExportedPrincipalId),
            ])
            .Verifiable();
    }

    private void SetupTarget()
    {
        MockTenantStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedTenant>(), CancellationToken.None))
            .Callback<PersistedTenant, CancellationToken>(
                (tenant, _) =>
                {
                    Calls.Add("tenant");
                    AddedTenants.Add(tenant);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedConnection>(), CancellationToken.None))
            .Callback<PersistedConnection, CancellationToken>(
                (connection, _) =>
                {
                    Calls.Add("connection");
                    AddedConnections.Add(connection);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockResourceServerStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedResourceServer>(), CancellationToken.None))
            .Callback<PersistedResourceServer, CancellationToken>(
                (resourceServer, _) =>
                {
                    Calls.Add("resource-server");
                    AddedResourceServers.Add(resourceServer);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        // The store gives each client a principal of its own.
        MockClientStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedClient>(), CancellationToken.None))
            .Callback<PersistedClient, CancellationToken>(
                (client, _) =>
                {
                    Calls.Add("client");
                    client.PrincipalId = ImportedPrincipalId;
                    AddedClients.Add(client);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockClientGrantStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedClientGrant>(), CancellationToken.None))
            .Callback<PersistedClientGrant, CancellationToken>(
                (grant, _) =>
                {
                    Calls.Add("client-grant");
                    AddedClientGrants.Add(grant);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockRoleAssignmentStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedRoleAssignment>(), CancellationToken.None))
            .Callback<PersistedRoleAssignment, CancellationToken>(
                (assignment, _) =>
                {
                    Calls.Add("role-assignment");
                    AddedRoleAssignments.Add(assignment);
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(CancellationToken.None))
            .Callback(() => Calls.Add("save"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private async ValueTask<TenantTransferSection> ExportSectionAsync(
        CancellationToken cancellationToken
    )
    {
        SetupStoreManager();
        SetupStores();
        SetupSource();

        return await CreateContributor().ExportAsync(TenantId, cancellationToken);
    }

    // Points one record group of an exported section at another tenant.
    private static TenantTransferSection WithAnotherTenant(
        TenantTransferSection section,
        string group
    )
    {
        var document = JsonNode.Parse(section.Json.GetRawText())!.AsObject();
        var record = group switch
        {
            "tenant" => document["tenant"]!,
            "settings" => document["tenant"]!["settings"]!,
            "secrets" => document["tenant"]!["secrets"]!,
            _ => document[group]![0]!,
        };
        record["tenantId"] = "tenant-2";
        return new TenantTransferSection
        {
            Name = section.Name,
            Json = JsonSerializer.SerializeToElement(document),
        };
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void Name_Always_IsCore()
    {
        Assert.Equal("core", CreateContributor().Name);
    }

    [Fact]
    public void Order_Always_IsZero()
    {
        Assert.Equal(0, CreateContributor().Order);
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenTheTenantDoesNotExist_Throws()
    {
        SetupStoreManager();
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, CancellationToken.None))
            .ReturnsAsync((PersistedTenant?)null)
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor().ExportAsync(TenantId, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ExportAsync_WhenTheTenantExists_WritesTheCoreRecordsAndNothingElse()
    {
        var section = await ExportSectionAsync(CancellationToken.None);

        Assert.Equal("core", section.Name);
        Assert.Equal(
            [
                "clientGrants",
                "clients",
                "connections",
                "resourceServers",
                "roleAssignments",
                "tenant",
            ],
            section.Json.EnumerateObject().Select(property => property.Name).Order().ToArray()
        );

        var tenant = section.Json.GetProperty("tenant");
        Assert.Equal(TenantId, tenant.GetProperty("tenantId").GetString());
        Assert.Equal("team-1", tenant.GetProperty("teamId").GetString());
        Assert.Equal("https://tenant-1.example.com", tenant.GetProperty("issuer").GetString());
        Assert.True(tenant.GetProperty("isDisabled").GetBoolean());
        Assert.Equal(
            """{"tenant-setting":1}""",
            tenant.GetProperty("settings").GetProperty("value").GetRawText()
        );

        // The secret value stays as its protected envelope.
        var tenantSecret = tenant.GetProperty("secrets").GetProperty("value")[0];
        Assert.Equal("tenant-envelope", tenantSecret.GetProperty("encodedValue").GetString());

        var connection = Assert.Single(section.Json.GetProperty("connections").EnumerateArray());
        Assert.Equal("local", connection.GetProperty("name").GetString());
        Assert.Equal("local", connection.GetProperty("strategy").GetString());

        var resourceServer = Assert.Single(
            section.Json.GetProperty("resourceServers").EnumerateArray()
        );
        Assert.Equal(
            "https://api.example.com",
            resourceServer.GetProperty("identifier").GetString()
        );
        Assert.Equal(
            "read",
            resourceServer.GetProperty("scopes")[0].GetProperty("value").GetString()
        );

        var client = Assert.Single(section.Json.GetProperty("clients").EnumerateArray());
        Assert.Equal("client-1", client.GetProperty("clientId").GetString());
        Assert.Equal(4, client.GetProperty("secrets").GetProperty("value").GetArrayLength());

        var grant = Assert.Single(section.Json.GetProperty("clientGrants").EnumerateArray());
        Assert.Equal("server-1", grant.GetProperty("resourceServerId").GetString());
        Assert.Equal(2, grant.GetProperty("scopes").GetArrayLength());

        Assert.Equal(2, section.Json.GetProperty("roleAssignments").GetArrayLength());
    }

    #endregion

    #region ImportAsync Tests

    [Fact]
    public async Task ImportAsync_WhenTheSectionIsAnotherSection_Throws()
    {
        var section = new TenantTransferSection { Name = "local-accounts", Json = Json("{}") };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenARecordGroupIsMissing_Throws()
    {
        var section = new TenantTransferSection { Name = "core", Json = Json("{}") };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheConnectionsPropertyIsMissing_Throws()
    {
        var section = await ExportSectionAsync(CancellationToken.None);
        var document = JsonNode.Parse(section.Json.GetRawText())!.AsObject();
        document.Remove("connections");
        var withoutConnections = new TenantTransferSection
        {
            Name = section.Name,
            Json = JsonSerializer.SerializeToElement(document),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, withoutConnections, CancellationToken.None)
        );

        Assert.Contains("'connections'", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenARecordGroupIsNull_Throws()
    {
        var section = new TenantTransferSection
        {
            Name = "core",
            Json = Json("""{"tenant":null}"""),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsAnotherTenant_Throws()
    {
        var section = await ExportSectionAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor().ImportAsync("tenant-2", section, CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("settings")]
    [InlineData("secrets")]
    [InlineData("connections")]
    [InlineData("resourceServers")]
    [InlineData("clients")]
    [InlineData("clientGrants")]
    [InlineData("roleAssignments")]
    public async Task ImportAsync_WhenARecordGroupHoldsAnotherTenant_ThrowsBeforeAnyStoreWrite(
        string group
    )
    {
        var section = await ExportSectionAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateContributor()
                .ImportAsync(TenantId, WithAnotherTenant(section, group), CancellationToken.None)
        );

        Assert.Contains("holds a record of tenant 'tenant-2'", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsRecords_AddsEachGroupAfterTheGroupItRefersTo()
    {
        var section = await ExportSectionAsync(CancellationToken.None);
        SetupTarget();

        await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None);

        Assert.Equal(
            [
                "tenant",
                "save",
                "connection",
                "save",
                "resource-server",
                "save",
                "client",
                "save",
                "client-grant",
                "role-assignment",
                "role-assignment",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheClientStoreGivesAClientAPrincipal_RoleAssignmentsFollowIt()
    {
        var section = await ExportSectionAsync(CancellationToken.None);
        SetupTarget();

        await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None);

        Assert.Collection(
            AddedRoleAssignments,
            assignment =>
            {
                Assert.Equal("assignment-1", assignment.AssignmentId);
                Assert.Equal(UserPrincipalId, assignment.PrincipalId);
            },
            assignment =>
            {
                Assert.Equal("assignment-2", assignment.AssignmentId);
                Assert.Equal(ImportedPrincipalId, assignment.PrincipalId);
            }
        );
        Assert.All(
            AddedRoleAssignments,
            assignment =>
            {
                Assert.Equal(TenantId, assignment.TenantId);
                Assert.Equal("owner", assignment.RoleName);
                Assert.Equal(ResourceNodeTypes.Tenant, assignment.ResourceType);
                Assert.Equal(TenantId, assignment.ResourceId);
                Assert.Equal(string.Empty, assignment.ConcurrencyToken);
            }
        );
    }

    [Fact]
    public async Task ImportAsync_WhenTheSectionHoldsRemoteSecrets_ReturnsTheirDistinctReferences()
    {
        var section = await ExportSectionAsync(CancellationToken.None);
        SetupTarget();

        var result = await CreateContributor()
            .ImportAsync(TenantId, section, CancellationToken.None);

        Assert.Equal(
            [
                new RemoteKeyReference("test-kms", "key-1"),
                new RemoteKeyReference("test-kms", "key-2"),
            ],
            result.RemoteKeys
        );
    }

    #endregion

    #region Round trip Tests

    [Fact]
    public async Task ImportAsync_AfterExportAsync_CarriesTheRecordsUnchanged()
    {
        var section = await ExportSectionAsync(CancellationToken.None);
        SetupTarget();

        await CreateContributor().ImportAsync(TenantId, section, CancellationToken.None);

        var source = CreateTenant();
        var tenant = Assert.Single(AddedTenants);
        Assert.Equal(source.TenantId, tenant.TenantId);
        Assert.Equal(source.TeamId, tenant.TeamId);
        Assert.Equal(source.Issuer, tenant.Issuer);
        Assert.Equal(source.IsDisabled, tenant.IsDisabled);
        Assert.Equal(source.DisplayName, tenant.DisplayName);
        Assert.Equal(source.Settings.Value.GetRawText(), tenant.Settings.Value.GetRawText());
        Assert.Equal(source.Secrets.Value, tenant.Secrets.Value);

        var sourceConnection = CreateConnection();
        var connection = Assert.Single(AddedConnections);
        Assert.Equal(sourceConnection.ConnectionId, connection.ConnectionId);
        Assert.Equal(sourceConnection.Name, connection.Name);
        Assert.Equal(sourceConnection.Strategy, connection.Strategy);
        Assert.Equal(sourceConnection.DisplayName, connection.DisplayName);
        Assert.Equal(sourceConnection.IsEnabled, connection.IsEnabled);
        Assert.Equal(sourceConnection.Options.GetRawText(), connection.Options.GetRawText());
        Assert.Equal(
            sourceConnection.Settings.Value.GetRawText(),
            connection.Settings.Value.GetRawText()
        );

        var sourceResourceServer = CreateResourceServer();
        var resourceServer = Assert.Single(AddedResourceServers);
        Assert.Equal(sourceResourceServer.ResourceServerId, resourceServer.ResourceServerId);
        Assert.Equal(sourceResourceServer.Identifier, resourceServer.Identifier);
        Assert.Equal(sourceResourceServer.Name, resourceServer.Name);
        Assert.Equal(
            sourceResourceServer.Settings.Value.GetRawText(),
            resourceServer.Settings.Value.GetRawText()
        );
        var scope = Assert.Single(resourceServer.Scopes);
        Assert.Equal("read", scope.Value);
        Assert.Equal("Reads", scope.Description);
        Assert.False(scope.IsSystem);
        Assert.Equal(sourceResourceServer.Secrets.Value, resourceServer.Secrets.Value);

        var sourceClient = CreateClient();
        var client = Assert.Single(AddedClients);
        Assert.Equal(sourceClient.ClientId, client.ClientId);
        Assert.Equal(sourceClient.IsDisabled, client.IsDisabled);
        Assert.Equal(sourceClient.ResourceServerId, client.ResourceServerId);
        Assert.Equal(sourceClient.Settings.Value.GetRawText(), client.Settings.Value.GetRawText());
        Assert.Equal(sourceClient.Secrets.Value, client.Secrets.Value);

        var grant = Assert.Single(AddedClientGrants);
        Assert.Equal("client-1", grant.ClientId);
        Assert.Equal("server-1", grant.ResourceServerId);
        Assert.Equal(["read", "write"], grant.Scopes);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_Always_DoesNothing()
    {
        await CreateContributor().RemoveAsync(TenantId, CancellationToken.None);
    }

    #endregion
}
