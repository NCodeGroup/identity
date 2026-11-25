// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Setup;

public sealed class CellSetupTests
{
    private const string TenantId = "cell-management";
    private const string CellName = "cell-eu-1";
    private const string PrincipalId = "principal-1";
    private const string Password = "a-root-password-of-15-or-more";
    private const string ConsoleClientId = "cell-console";
    private const string ConsoleJwks = """{"keys":[{"kty":"EC","crv":"P-256","x":"x","y":"y"}]}""";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; } = new();
    private Mock<IStoreManager> MockStoreManager { get; } = new();
    private Mock<IInstanceSetupStore> MockInstanceSetupStore { get; } = new();
    private Mock<IRoleAssignmentStore> MockRoleAssignmentStore { get; } = new();
    private Mock<IConnectionStore> MockConnectionStore { get; } = new();
    private Mock<ITenantResolver> MockTenantResolver { get; } = new();
    private Mock<IServiceScopeFactory> MockServiceScopeFactory { get; } = new();
    private Mock<IServiceScope> MockServiceScope { get; } = new();
    private Mock<IServiceProvider> MockServiceProvider { get; } = new();
    private Mock<ITenantProvisioner> MockTenantProvisioner { get; } = new();
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; } = new();
    private Mock<ILocalAccountCreator> MockLocalAccountCreator { get; } = new();
    private Mock<IClientCreator> MockClientCreator { get; } = new();
    private Mock<ICryptoService> MockCryptoService { get; } = new();
    private List<PersistedRoleAssignment> Assignments { get; } = [];
    private List<ClientCreationRequest> ClientRequests { get; } = [];

    private DefaultInstanceSetupService CreateService(bool isControlPlane, bool hasManagementTenant)
    {
        var topology = new Mock<IPlatformTopology>();
        topology.Setup(x => x.IsControlPlaneCell).Returns(isControlPlane);
        topology.Setup(x => x.HasManagementTenant).Returns(hasManagementTenant);

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        MockStoreManager
            .Setup(x => x.GetStore<IInstanceSetupStore>())
            .Returns(MockInstanceSetupStore.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IRoleAssignmentStore>())
            .Returns(MockRoleAssignmentStore.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object);

        MockInstanceSetupStore
            .Setup(x => x.GetOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedInstanceSetup
                {
                    Key = PersistedInstanceSetup.SingletonKey,
                    IsInitialized = false,
                    ConcurrencyToken = "token",
                }
            );
        MockInstanceSetupStore
            .Setup(x =>
                x.UpdateAsync(It.IsAny<PersistedInstanceSetup>(), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask);
        MockRoleAssignmentStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedRoleAssignment>(), It.IsAny<CancellationToken>())
            )
            .Callback<PersistedRoleAssignment, CancellationToken>(
                (assignment, _) => Assignments.Add(assignment)
            )
            .Returns(ValueTask.CompletedTask);
        MockCryptoService
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("assignment-1");

        MockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(MockServiceScope.Object);
        MockServiceScope.Setup(x => x.ServiceProvider).Returns(MockServiceProvider.Object);
        MockServiceProvider
            .Setup(x => x.GetService(typeof(ITenantProvisioner)))
            .Returns(MockTenantProvisioner.Object);
        MockTenantProvisioner
            .Setup(x => x.SeedAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersistedTenantBuilder { TenantId = TenantId }.Build());
        MockTenantResolver
            .Setup(x =>
                x.ResolveControlPlaneTenantAsync(
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new PersistedTenantBuilder
                {
                    TenantId = TenantId,
                    Issuer = "https://cell.example.test/",
                }.Build()
            );
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(It.IsAny<string>()))
            .Returns(new Mock<IDisposable>().Object);
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(new Mock<IDisposable>().Object);

        MockConnectionStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(ConnectionDefaults.Name, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new PersistedConnection
                {
                    TenantId = TenantId,
                    ConnectionId = "local-connection-id",
                    Name = ConnectionDefaults.Name,
                    Strategy = ConnectionStrategyNames.Local,
                    DisplayName = "Username and password",
                    IsSystem = false,
                    IsEnabled = true,
                    Options = JsonSerializer.SerializeToElement(new { }),
                    Settings = new PersistedConnectionSettings
                    {
                        TenantId = TenantId,
                        ConnectionId = "local-connection-id",
                        Value = JsonSerializer.SerializeToElement(new { }),
                    },
                }
            );
        MockLocalAccountCreator
            .Setup(x =>
                x.StageAsync(
                    MockStoreManager.Object,
                    It.IsAny<PersistedConnection>(),
                    It.IsAny<CreateLocalAccountRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new LocalAccountCreation
                {
                    PrincipalId = PrincipalId,
                    Account = new PersistedLocalAccount
                    {
                        TenantId = TenantId,
                        ConnectionId = "local-connection-id",
                        LocalAccountId = "account-1",
                        UserName = "admin",
                        DisplayName = "admin",
                        Email = null,
                        EmailVerified = false,
                        IsEnabled = true,
                        Claims = [],
                        ConcurrencyToken = string.Empty,
                    },
                }
            );
        MockClientCreator
            .Setup(x =>
                x.StageAsync(
                    MockStoreManager.Object,
                    It.IsAny<ClientCreationRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<IStoreManager, ClientCreationRequest, CancellationToken>(
                (_, request, _) => ClientRequests.Add(request)
            )
            .ReturnsAsync(
                ClientCreation.Staged(
                    new PersistedClient
                    {
                        TenantId = TenantId,
                        ClientId = ConsoleClientId,
                        ConcurrencyToken = string.Empty,
                        IsDisabled = false,
                        Settings = new PersistedClientSettings
                        {
                            TenantId = TenantId,
                            ClientId = ConsoleClientId,
                            ConcurrencyToken = string.Empty,
                            Value = JsonSerializer.SerializeToElement(new { }),
                        },
                        Secrets = new PersistedClientSecrets
                        {
                            TenantId = TenantId,
                            ClientId = ConsoleClientId,
                            ConcurrencyToken = string.Empty,
                            Value = [],
                        },
                    }
                )
            );

        return new DefaultInstanceSetupService(
            MockStoreManagerFactory.Object,
            MockTenantResolver.Object,
            MockServiceScopeFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockLocalAccountCreator.Object,
            MockClientCreator.Object,
            MockCryptoService.Object,
            topology.Object,
            Options.Create(new CellOptions { Name = CellName, IsControlPlane = isControlPlane }),
            Options.Create(new SetupInstanceLinksOptions()),
            new FakeTimeProvider(),
            new Mock<ILogger<DefaultInstanceSetupService>>().Object
        );
    }

    private static InstanceSetupRequest CreateRequest() =>
        new()
        {
            Root = new SetupRootOptions { UserName = "admin", Password = Password },
            ManagementConsole = new SetupManagementConsoleOptions
            {
                ClientId = ConsoleClientId,
                BaseUrl = "https://cell-console.example.test/",
                Jwks = ConsoleJwks,
            },
        };

    [Fact]
    public async Task SetUpAsync_InACellWithAManagementTenant_GrantsCellAdminAtTheCellNode()
    {
        var service = CreateService(isControlPlane: false, hasManagementTenant: true);

        var result = await service.SetUpAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var assignment = Assert.Single(Assignments);
        Assert.Equal(BuiltInRoles.CellAdmin, assignment.RoleName);
        Assert.Equal(OpenIdResourceTypes.Cell, assignment.ResourceType);
        Assert.Equal(CellName, assignment.ResourceId);
        Assert.Equal(PrincipalId, assignment.PrincipalId);
        Assert.DoesNotContain(Assignments, item => item.RoleName == BuiltInRoles.GlobalAdmin);
    }

    [Fact]
    public async Task SetUpAsync_InACellWithAManagementTenant_RegistersTheCellConsoleClient()
    {
        var service = CreateService(isControlPlane: false, hasManagementTenant: true);

        var result = await service.SetUpAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.Single(ClientRequests);
        Assert.Equal(ConsoleClientId, request.ClientId);
        Assert.Equal(TenantId, request.TenantId);
        Assert.Equal(ConsoleClientId, result.Root.ManagementConsoleClientId);
    }

    [Fact]
    public async Task SetUpAsync_InTheControlPlaneCell_GrantsGlobalAdminAtThePlatformNode()
    {
        var service = CreateService(isControlPlane: true, hasManagementTenant: false);

        var result = await service.SetUpAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var assignment = Assert.Single(Assignments);
        Assert.Equal(BuiltInRoles.GlobalAdmin, assignment.RoleName);
        Assert.Equal(OpenIdResourceTypes.Platform, assignment.ResourceType);
    }

    [Fact]
    public async Task SetUpAsync_InAWorkloadCellWithoutAManagementTenant_IsRefused()
    {
        var service = CreateService(isControlPlane: false, hasManagementTenant: false);

        var result = await service.SetUpAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Error.StatusCode);
        Assert.Empty(Assignments);
        Assert.Empty(ClientRequests);
    }
}
