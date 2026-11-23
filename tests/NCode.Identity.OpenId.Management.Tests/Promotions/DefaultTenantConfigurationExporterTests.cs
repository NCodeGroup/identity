// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Management.Promotions;

public sealed class DefaultTenantConfigurationExporterTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string Region = "us";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IClientStore> MockClientStore { get; }
    private Mock<IClientGrantStore> MockClientGrantStore { get; }
    private Mock<IResourceServerStore> MockResourceServerStore { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IHostManagedClientChecker> MockHostManagedClientChecker { get; }

    public DefaultTenantConfigurationExporterTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockClientStore = MockRepository.Create<IClientStore>();
        MockClientGrantStore = MockRepository.Create<IClientGrantStore>();
        MockResourceServerStore = MockRepository.Create<IResourceServerStore>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockHostManagedClientChecker = MockRepository.Create<IHostManagedClientChecker>();
    }

    public void Dispose() => MockRepository.Verify();

    private DefaultTenantConfigurationExporter CreateExporter() =>
        new(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockHostManagedClientChecker.Object,
            Options.Create(new CellOptions { Name = "cell-1", Region = Region })
        );

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(CancellationToken.None))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
    }

    private void SetupChildStores()
    {
        MockStoreManager
            .Setup(x => x.GetStore<IClientStore>())
            .Returns(MockClientStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IClientGrantStore>())
            .Returns(MockClientGrantStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IResourceServerStore>())
            .Returns(MockResourceServerStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
    }

    private void SetupScope()
    {
        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(scope.Object)
            .Verifiable();
    }

    private void SetupTenant(PersistedTenant? tenant) =>
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, CancellationToken.None))
            .ReturnsAsync(tenant)
            .Verifiable();

    private void SetupHostManaged(string clientId, bool isHostManaged) =>
        MockHostManagedClientChecker
            .Setup(x => x.IsHostManaged(TenantId, clientId))
            .Returns(isHostManaged)
            .Verifiable();

    private void SetupClientPage(
        string? cursor,
        string? nextCursor,
        params PersistedClient[] clients
    ) =>
        MockClientStore
            .Setup(x =>
                x.GetPageAsync(
                    It.Is<PageRequest>(request => request.Cursor == cursor && request.Limit == 100),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedClient> { Items = clients, NextCursor = nextCursor }
            )
            .Verifiable();

    private void SetupGrants(string clientId, params PersistedClientGrant[] grants) =>
        MockClientGrantStore
            .Setup(x =>
                x.GetPageAsync(
                    clientId,
                    It.Is<PageRequest>(request => request.Cursor == null && request.Limit == 100),
                    CancellationToken.None
                )
            )
            .ReturnsAsync(
                new PagedResult<PersistedClientGrant> { Items = grants, NextCursor = null }
            )
            .Verifiable();

    private void SetupResourceServers(params PersistedResourceServer[] resourceServers) =>
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
                    Items = resourceServers,
                    NextCursor = null,
                }
            )
            .Verifiable();

    private void SetupConnections(params PersistedConnection[] connections) =>
        MockConnectionStore
            .Setup(x => x.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(connections)
            .Verifiable();

    private static PersistedConnection CreateConnection(string name) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = $"id-of-{name}",
            Name = name,
            Strategy = "local",
            DisplayName = $"Display of {name}",
            IsSystem = false,
            IsEnabled = false,
            Options = Json("""{"store":"default"}"""),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = $"id-of-{name}",
                Value = Json("""{"connection-setting":4}"""),
            },
        };

    private static JsonElement Json(string json) => JsonElement.Parse(json);

    private static PersistedTenant CreateTenant(string settings = "{}") =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            ConcurrencyToken = "tenant-concurrency-token",
            Settings = Json(settings),
            Secrets =
            [
                new PersistedSecretBuilder
                {
                    SecretId = "tenant-secret-id",
                    EncodedValue = "tenant-secret-material",
                }.Build(),
            ],
        }.Build();

    private static PersistedClient CreateClient(
        string clientId,
        string? resourceServerId = null,
        bool isDisabled = false,
        string settings = "{}",
        string? owningNodeType = null
    ) =>
        new PersistedClientBuilder
        {
            TenantId = TenantId,
            ClientId = clientId,
            ConcurrencyToken = "client-concurrency-token",
            PrincipalId = "client-principal-id",
            IsDisabled = isDisabled,
            ResourceServerId = resourceServerId,
            OwningNodeType = owningNodeType,
            OwningNodeId = owningNodeType is null ? null : "owner-1",
            Settings = Json(settings),
            Secrets =
            [
                new PersistedSecretBuilder
                {
                    SecretId = "client-secret-id",
                    EncodedValue = "client-secret-material",
                }.Build(),
            ],
        }.Build();

    private static PersistedClientGrant CreateGrant(
        string clientId,
        string resourceServerId,
        params string[] scopes
    ) =>
        new()
        {
            TenantId = TenantId,
            ClientId = clientId,
            ResourceServerId = resourceServerId,
            ConcurrencyToken = "grant-concurrency-token",
            Scopes = scopes,
        };

    private static PersistedResourceServer CreateResourceServer(
        string resourceServerId,
        string identifier,
        bool isSystem = false,
        bool isDisabled = false,
        string settings = "{}",
        params PersistedScope[] scopes
    ) =>
        new PersistedResourceServerBuilder
        {
            TenantId = TenantId,
            ResourceServerId = resourceServerId,
            Identifier = identifier,
            Name = $"Name of {resourceServerId}",
            IsSystem = isSystem,
            IsDisabled = isDisabled,
            ConcurrencyToken = "resource-server-concurrency-token",
            Settings = Json(settings),
            Scopes = scopes,
            Secrets =
            [
                new PersistedSecretBuilder
                {
                    SecretId = "resource-server-secret-id",
                    EncodedValue = "resource-server-secret-material",
                }.Build(),
            ],
        }.Build();

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenTheTenantDoesNotExist_ReturnsNull()
    {
        SetupStoreManager();
        SetupScope();
        SetupTenant(null);

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        Assert.Null(document);
    }

    [Fact]
    public async Task ExportAsync_WhenTheTenantExists_ReturnsItsConfigurationStampedWithTheRegion()
    {
        SetupStoreManager();
        SetupChildStores();
        SetupScope();
        SetupTenant(CreateTenant("""{"tenant-setting":1}"""));
        SetupClientPage(
            null,
            null,
            CreateClient("client-1", "server-1", isDisabled: true, """{"client-setting":2}""")
        );
        SetupHostManaged("client-1", false);
        SetupGrants("client-1", CreateGrant("client-1", "server-1", "read", "write"));
        SetupResourceServers(
            CreateResourceServer(
                "server-1",
                "https://api.example.com",
                isSystem: false,
                isDisabled: true,
                """{"server-setting":3}""",
                new PersistedScope
                {
                    Value = "read",
                    Description = "Reads",
                    IsSystem = false,
                },
                new PersistedScope
                {
                    Value = "write",
                    Description = null,
                    IsSystem = true,
                }
            )
        );

        SetupConnections(CreateConnection("corp"));

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(TenantId, document.SourceTenantId);
        Assert.Equal(Region, document.Region);
        var connection = Assert.Single(document.Connections);
        Assert.Equal("corp", connection.Name);
        Assert.Equal("local", connection.Strategy);
        Assert.Equal("Display of corp", connection.DisplayName);
        Assert.False(connection.IsEnabled);
        Assert.Equal("""{"store":"default"}""", connection.Options.GetRawText());
        Assert.Equal("""{"connection-setting":4}""", connection.Settings.GetRawText());
        Assert.Equal("""{"tenant-setting":1}""", document.Settings.GetRawText());

        var client = Assert.Single(document.Clients);
        Assert.Equal("client-1", client.ClientId);
        Assert.True(client.IsDisabled);
        Assert.Equal("server-1", client.ResourceServerId);
        Assert.Equal("""{"client-setting":2}""", client.Settings.GetRawText());

        var grant = Assert.Single(document.ClientGrants);
        Assert.Equal("client-1", grant.ClientId);
        Assert.Equal("server-1", grant.ResourceServerId);
        Assert.Equal(["read", "write"], grant.Scopes);

        var resourceServer = Assert.Single(document.ResourceServers);
        Assert.Equal("server-1", resourceServer.ResourceServerId);
        Assert.Equal("https://api.example.com", resourceServer.Identifier);
        Assert.Equal("Name of server-1", resourceServer.Name);
        Assert.False(resourceServer.IsSystem);
        Assert.True(resourceServer.IsDisabled);
        Assert.Equal("""{"server-setting":3}""", resourceServer.Settings.GetRawText());
        Assert.Collection(
            resourceServer.Scopes,
            scope =>
            {
                Assert.Equal("read", scope.Value);
                Assert.Equal("Reads", scope.Description);
                Assert.False(scope.IsSystem);
            },
            scope =>
            {
                Assert.Equal("write", scope.Value);
                Assert.Null(scope.Description);
                Assert.True(scope.IsSystem);
            }
        );
    }

    [Fact]
    public async Task ExportAsync_WhenTheTenantHoldsSecrets_ExportsNoSecretNoTokenAndNoPrincipal()
    {
        SetupStoreManager();
        SetupChildStores();
        SetupScope();
        SetupTenant(CreateTenant());
        SetupClientPage(null, null, CreateClient("client-1"));
        SetupHostManaged("client-1", false);
        SetupGrants("client-1");
        SetupResourceServers(CreateResourceServer("server-1", "https://api.example.com"));
        SetupConnections();

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        var json = JsonSerializer.Serialize(document);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrency", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("principal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"TenantId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WhenAClientIsAMachineClient_LeavesOutTheClientAndItsGrants()
    {
        SetupStoreManager();
        SetupChildStores();
        SetupScope();
        SetupTenant(CreateTenant());
        SetupClientPage(
            null,
            null,
            CreateClient("machine-client", owningNodeType: "team"),
            CreateClient("client-1")
        );
        SetupHostManaged("client-1", false);
        SetupGrants("client-1", CreateGrant("client-1", "server-1", "read"));
        SetupResourceServers();
        SetupConnections();

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(["client-1"], document.Clients.Select(x => x.ClientId));
        Assert.Equal(["client-1"], document.ClientGrants.Select(x => x.ClientId));
    }

    [Fact]
    public async Task ExportAsync_WhenAClientIsHostManaged_LeavesOutTheClientAndItsGrants()
    {
        SetupStoreManager();
        SetupChildStores();
        SetupScope();
        SetupTenant(CreateTenant());
        SetupClientPage(null, null, CreateClient("host-client"), CreateClient("client-1"));
        SetupHostManaged("host-client", true);
        SetupHostManaged("client-1", false);
        SetupGrants("client-1", CreateGrant("client-1", "server-1", "read"));
        SetupResourceServers();
        SetupConnections();

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(["client-1"], document.Clients.Select(x => x.ClientId));
        Assert.Equal(["client-1"], document.ClientGrants.Select(x => x.ClientId));
    }

    [Fact]
    public async Task ExportAsync_WhenClientsSpanSeveralPages_ExportsEveryPage()
    {
        SetupStoreManager();
        SetupChildStores();
        SetupScope();
        SetupTenant(CreateTenant());
        SetupClientPage(null, "next", CreateClient("client-1"));
        SetupClientPage("next", null, CreateClient("client-2"));
        SetupHostManaged("client-1", false);
        SetupHostManaged("client-2", false);
        SetupGrants("client-1");
        SetupGrants("client-2");
        SetupResourceServers();
        SetupConnections();

        var document = await CreateExporter().ExportAsync(TenantId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(["client-1", "client-2"], document.Clients.Select(x => x.ClientId));
    }

    #endregion
}
