// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management;
using NCode.Identity.OpenId.Accounts.Management.Auditing;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Setup;

public sealed class DefaultAdministratorRecoveryServiceTests : IDisposable
{
    private const string TenantId = "control-plane";
    private const string ConnectionId = "local-connection-id";
    private const string PrincipalId = "principal-1";
    private const string LocalAccountId = "account-1";
    private const string AssignmentId = "assignment-1";
    private const string UserName = "admin";
    private const string Password = "a-recovery-password-of-15-or-more";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IFederatedIdentityStore> MockIdentityStore { get; }
    private Mock<IRoleAssignmentStore> MockRoleAssignmentStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<ILocalAccountTransferStore> MockTransferStore { get; }
    private Mock<ITenantResolver> MockTenantResolver { get; }
    private Mock<IServiceScopeFactory> MockServiceScopeFactory { get; }
    private Mock<IServiceScope> MockServiceScope { get; }
    private Mock<IServiceProvider> MockServiceProvider { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IDisposable> MockUnscopedScope { get; }
    private Mock<IDisposable> MockTenantScope { get; }
    private Mock<ILocalAccountCreator> MockLocalAccountCreator { get; }
    private Mock<IPasswordAttemptLimiter> MockPasswordAttemptLimiter { get; }
    private Mock<IManagementAuditRecorder> MockAuditRecorder { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private List<ResourceChangeAuditEvent> AuditEvents { get; } = [];

    public DefaultAdministratorRecoveryServiceTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockIdentityStore = MockRepository.Create<IFederatedIdentityStore>();
        MockRoleAssignmentStore = MockRepository.Create<IRoleAssignmentStore>();
        MockLocalAccountStore = MockRepository.Create<ILocalAccountStore>();
        MockTransferStore = MockRepository.Create<ILocalAccountTransferStore>();
        MockTenantResolver = MockRepository.Create<ITenantResolver>();
        MockServiceScopeFactory = MockRepository.Create<IServiceScopeFactory>();
        MockServiceScope = MockRepository.Create<IServiceScope>();
        MockServiceProvider = MockRepository.Create<IServiceProvider>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockUnscopedScope = MockRepository.Create<IDisposable>();
        MockTenantScope = MockRepository.Create<IDisposable>();
        MockLocalAccountCreator = MockRepository.Create<ILocalAccountCreator>();
        MockPasswordAttemptLimiter = MockRepository.Create<IPasswordAttemptLimiter>();
        MockAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private DefaultAdministratorRecoveryService CreateService()
    {
        var registration = new LocalAccountStoreRegistration
        {
            Name = LocalAccountStoreNames.Default,
            CreateStore = _ => MockLocalAccountStore.Object,
            CreateTransferStore = _ => MockTransferStore.Object,
        };
        return new DefaultAdministratorRecoveryService(
            MockStoreManagerFactory.Object,
            MockTenantResolver.Object,
            MockServiceScopeFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockLocalAccountCreator.Object,
            new LocalAccountStoreCatalog([registration]),
            MockPasswordAttemptLimiter.Object,
            MockAuditRecorder.Object,
            MockCryptoService.Object,
            NullLogger<DefaultAdministratorRecoveryService>.Instance
        );
    }

    private static RecoveryAdminOptions CreateOptions(
        string? userName = UserName,
        string? password = Password
    ) =>
        new()
        {
            UserName = userName,
            Password = password,
            Email = "admin@example.com",
        };

    private static PersistedConnection CreateLocalConnection() =>
        new()
        {
            TenantId = TenantId,
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
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                Value = JsonElements.EmptyObject,
            },
        };

    private static PersistedLocalAccount CreateAccount(bool isEnabled) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = ConnectionId,
            LocalAccountId = LocalAccountId,
            UserName = UserName,
            DisplayName = UserName,
            Email = null,
            EmailVerified = false,
            IsEnabled = isEnabled,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private static PersistedRoleAssignment CreateAssignment(string principalId, string roleName) =>
        new()
        {
            TenantId = TenantId,
            AssignmentId = "existing",
            PrincipalId = principalId,
            RoleName = roleName,
            ResourceType = OpenIdResourceTypes.Platform,
            ResourceId = OpenIdConstants.PlatformResourceId,
            ConcurrencyToken = string.Empty,
        };

    private void SetupTenantAndStores()
    {
        MockServiceScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(MockServiceScope.Object)
            .Verifiable(Times.Once());
        MockServiceScope.Setup(x => x.ServiceProvider).Returns(MockServiceProvider.Object);
        MockServiceScope.Setup(x => x.Dispose()).Verifiable(Times.Once());
        MockTenantResolver
            .Setup(x =>
                x.ResolveControlPlaneTenantAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new PersistedTenantBuilder
                {
                    TenantId = TenantId,
                    Issuer = "https://id.example.test/",
                }.Build()
            )
            .Verifiable(Times.Once());
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("administrator recovery"))
            .Returns(MockUnscopedScope.Object)
            .Verifiable(Times.Once());
        MockUnscopedScope.Setup(x => x.Dispose()).Verifiable(Times.Once());
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(MockTenantScope.Object)
            .Verifiable(Times.Once());
        MockTenantScope.Setup(x => x.Dispose()).Verifiable(Times.Once());

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable(Times.Once());
        MockStoreManager
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object);
        MockConnectionStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(ConnectionDefaults.Name, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateLocalConnection())
            .Verifiable(Times.Once());
    }

    private void SetupAccount(PersistedLocalAccount? account)
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(ConnectionId, UserName, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(account)
            .Verifiable(Times.Once());
    }

    private void SetupIdentity(string? principalId)
    {
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockIdentityStore.Object);
        var identity = principalId is null
            ? null
            : new PersistedFederatedIdentity
            {
                TenantId = TenantId,
                FederatedIdentityId = "identity-1",
                PrincipalId = principalId,
                ConnectionId = ConnectionId,
                Issuer = null,
                Subject = LocalAccountId,
                JoinKey = null,
                ConcurrencyToken = string.Empty,
            };
        MockIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    ConnectionId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identity)
            .Verifiable(Times.Once());
    }

    private void SetupRoleStore(
        IReadOnlyList<PersistedRoleAssignment> existing,
        List<PersistedRoleAssignment> added
    )
    {
        MockStoreManager
            .Setup(x => x.GetStore<IRoleAssignmentStore>())
            .Returns(MockRoleAssignmentStore.Object);
        MockRoleAssignmentStore
            .Setup(x =>
                x.GetByResourceAsync(
                    OpenIdResourceTypes.Platform,
                    OpenIdConstants.PlatformResourceId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(existing)
            .Verifiable(Times.Once());
        MockCryptoService
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns(AssignmentId);
        MockRoleAssignmentStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedRoleAssignment>(), It.IsAny<CancellationToken>())
            )
            .Callback<PersistedRoleAssignment, CancellationToken>(
                (assignment, _) => added.Add(assignment)
            )
            .Returns(ValueTask.CompletedTask);
    }

    private void SetupSaveAndReset()
    {
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.ResetAsync(
                    TenantId,
                    ConnectionId,
                    LocalAccountAttemptKeys.ForAccount(LocalAccountId),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        MockAuditRecorder
            .Setup(x =>
                x.RecordResourceChangeAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<ResourceChangeAuditEvent>()
                )
            )
            .Callback<HttpContext, ResourceChangeAuditEvent>((_, item) => AuditEvents.Add(item))
            .Returns(ValueTask.CompletedTask);
    }

    #endregion

    [Fact]
    public async Task RecoverAsync_NoUserName_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.RecoverAsync(
            CreateOptions(userName: " "),
            CancellationToken.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
    }

    [Fact]
    public async Task RecoverAsync_ShortPassword_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.RecoverAsync(
            CreateOptions(password: "short"),
            CancellationToken.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
    }

    [Fact]
    public async Task RecoverAsync_DisabledAccount_EnablesItSetsThePasswordAndGrantsGlobalAdmin()
    {
        var account = CreateAccount(isEnabled: false);
        var added = new List<PersistedRoleAssignment>();
        SetupTenantAndStores();
        SetupAccount(account);
        SetupIdentity(PrincipalId);
        SetupRoleStore([], added);
        SetupSaveAndReset();
        string? storedPassword = null;
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(item => item.IsEnabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        MockLocalAccountStore
            .Setup(x =>
                x.SetPasswordAsync(
                    LocalAccountId,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ReadOnlyMemory<byte>, CancellationToken>(
                (_, bytes, _) => storedPassword = SecureEncoding.UTF8.GetString(bytes.Span)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        var service = CreateService();

        var result = await service.RecoverAsync(CreateOptions(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Created);
        Assert.Equal(PrincipalId, result.PrincipalId);
        Assert.Equal(TenantId, result.TenantId);
        Assert.True(account.IsEnabled);
        Assert.Equal(Password, storedPassword);
        var assignment = Assert.Single(added);
        Assert.Equal(BuiltInRoles.GlobalAdmin, assignment.RoleName);
        Assert.Equal(PrincipalId, assignment.PrincipalId);
        Assert.Equal(AssignmentId, assignment.AssignmentId);
        Assert.Equal(2, AuditEvents.Count);
        Assert.IsType<LocalAccountChangedAuditEvent>(AuditEvents[0]);
        Assert.Equal(ResourceChangeTypes.Updated, AuditEvents[0].ChangeType);
        Assert.IsType<RoleAssignmentChangedAuditEvent>(AuditEvents[1]);
    }

    [Fact]
    public async Task RecoverAsync_AccountThatAlreadyHoldsTheRole_AddsNoAssignment()
    {
        var account = CreateAccount(isEnabled: true);
        var added = new List<PersistedRoleAssignment>();
        SetupTenantAndStores();
        SetupAccount(account);
        SetupIdentity(PrincipalId);
        SetupRoleStore([CreateAssignment(PrincipalId, BuiltInRoles.GlobalAdmin)], added);
        SetupSaveAndReset();
        MockLocalAccountStore
            .Setup(x =>
                x.SetPasswordAsync(
                    LocalAccountId,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
        var service = CreateService();

        var result = await service.RecoverAsync(CreateOptions(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(added);
        Assert.Single(AuditEvents);
    }

    [Fact]
    public async Task RecoverAsync_NoAccount_CreatesItAndGrantsGlobalAdmin()
    {
        var added = new List<PersistedRoleAssignment>();
        SetupTenantAndStores();
        SetupAccount(null);
        SetupRoleStore([], added);
        SetupSaveAndReset();
        var creation = new LocalAccountCreation
        {
            PrincipalId = PrincipalId,
            Account = CreateAccount(isEnabled: true),
        };
        MockLocalAccountCreator
            .Setup(x =>
                x.StageAsync(
                    MockStoreManager.Object,
                    It.IsAny<PersistedConnection>(),
                    It.Is<CreateLocalAccountRequest>(request =>
                        request.UserName == UserName
                        && request.Password == Password
                        && request.IsEnabled
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(creation)
            .Verifiable(Times.Once());
        var service = CreateService();

        var result = await service.RecoverAsync(CreateOptions(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Created);
        Assert.Single(added);
        Assert.Equal(ResourceChangeTypes.Created, AuditEvents[0].ChangeType);
    }

    [Fact]
    public async Task RecoverAsync_AccountWithoutAPrincipal_ReturnsConflict()
    {
        SetupTenantAndStores();
        SetupAccount(CreateAccount(isEnabled: true));
        SetupIdentity(null);
        var service = CreateService();

        var result = await service.RecoverAsync(CreateOptions(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, result.Error.StatusCode);
    }
}
