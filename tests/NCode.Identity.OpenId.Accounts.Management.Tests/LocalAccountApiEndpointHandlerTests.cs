// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Moq.Protected;
using NCode.Identity.AspNetCore;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using NCode.Mediator;
using SystemTextJsonPatch;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class LocalAccountApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ControlPlaneTenantId = "control-plane";
    private const string TeamId = "team-1";
    private const string ConnectionId = "connection-1";
    private const string OtherConnectionId = "connection-2";
    private const string LocalAccountEmail = "alice@example.com";
    private const string LocalAccountId = "account-1";
    private const string UserName = "alice";
    private const string DisplayName = "Alice";
    private const string Password = "a-long-unlisted-passphrase";
    private const string PrincipalIdNotChecked = "<principal id not checked>";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<IFederatedPrincipalStore> MockFederatedPrincipalStore { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<ILocalAccountValidator> MockLocalAccountValidator { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private Mock<ISessionService> MockSessionService { get; }
    private Mock<IOpenIdContextFactory> MockOpenIdContextFactory { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IPasswordPolicy> MockPasswordPolicy { get; }
    private Mock<IPostCommitSecurityEffects> MockPostCommitSecurityEffects { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private PersistedConnection Connection { get; }
    private LocalAccountApiEndpointHandler Handler { get; }
    private List<string> Calls { get; } = [];

    public LocalAccountApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockLocalAccountStore = MockRepository.Create<ILocalAccountStore>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockFederatedIdentityStore = MockRepository.Create<IFederatedIdentityStore>();
        MockFederatedPrincipalStore = MockRepository.Create<IFederatedPrincipalStore>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockLocalAccountValidator = MockRepository.Create<ILocalAccountValidator>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        MockSessionService = MockRepository.Create<ISessionService>();
        MockOpenIdContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        MockPasswordPolicy = MockRepository.Create<IPasswordPolicy>();
        MockPostCommitSecurityEffects = MockRepository.Create<IPostCommitSecurityEffects>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();

        Connection = CreateConnection(ConnectionId);
        var localAccountStoreCatalog = new LocalAccountStoreCatalog([
            new LocalAccountStoreRegistration
            {
                Name = "default",
                CreateStore = _ => MockLocalAccountStore.Object,
                CreateTransferStore = _ =>
                    MockRepository.Create<ILocalAccountTransferStore>().Object,
            },
        ]);

        var localAccountPasswords = new LocalAccountPasswords(
            MockEffectiveSettingsResolver.Object,
            MockPasswordPolicy.Object
        );

        Handler = new LocalAccountApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockLocalAccountValidator.Object,
            new DefaultLocalAccountCreator(
                MockLocalAccountValidator.Object,
                MockCryptoService.Object,
                localAccountPasswords,
                localAccountStoreCatalog
            ),
            MockManagementAuditRecorder.Object,
            MockSessionService.Object,
            MockOpenIdContextFactory.Object,
            localAccountPasswords,
            MockPostCommitSecurityEffects.Object,
            MockAmbientTenantAccessor.Object,
            localAccountStoreCatalog
        );
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private void SetupAuthorizeCreate(ManagementError? error)
    {
        MockLocalAccountValidator
            .Setup(x =>
                x.AuthorizeCreateAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    TenantId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(error)
            .Verifiable();
    }

    private void SetupAuthorizeUpdate(ManagementError? error)
    {
        MockLocalAccountValidator
            .Setup(x =>
                x.AuthorizeUpdateAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    TenantId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(error)
            .Verifiable();
    }

    private void SetupAuthorizeDelete(ManagementError? error)
    {
        MockLocalAccountValidator
            .Setup(x =>
                x.AuthorizeDeleteAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    TenantId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(error)
            .Verifiable();
    }

    private static ManagementError CreateForbiddenError() =>
        new()
        {
            StatusCode = StatusCodes.Status403Forbidden,
            Detail = "The caller is not authorized.",
        };

    private void SetupConnection(
        PersistedConnection? connection = null,
        string connectionId = ConnectionId
    )
    {
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection ?? Connection)
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

    private void SetupStoreManagerCreated()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    // Mocks the store manager and reads the account; the store manager is only created once authorization succeeded.
    private void SetupAccount(PersistedLocalAccount? account)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        SetupConnection();
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

    private void SetupDeleteValidated()
    {
        MockLocalAccountValidator
            .Setup(x =>
                x.ValidateDeleteAsync(
                    It.Is<PersistedLocalAccount>(account =>
                        account.LocalAccountId == LocalAccountId
                    ),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
    }

    // The password is judged by the connection's effective settings, resolved from the stored documents.
    private void SetupPasswordAccepted()
    {
        var tenant = CreateTenant();
        var teamSettings = Mock.Of<IReadOnlySettingCollection>();
        var tenantSettings = Mock.Of<IReadOnlySettingCollection>();
        var connectionSettings = Mock.Of<IReadOnlySettingCollection>();

        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamSettings)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    teamSettings,
                    OpenIdSettingScopes.Tenant,
                    TenantId,
                    It.Is<JsonElement>(element => element.GetRawText() == "{}")
                )
            )
            .Returns(tenantSettings)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    tenantSettings,
                    OpenIdSettingScopes.Connection,
                    ConnectionId,
                    It.Is<JsonElement>(element => element.GetRawText() == "{}")
                )
            )
            .Returns(connectionSettings)
            .Verifiable();
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(context =>
                        ReferenceEquals(context.Settings, connectionSettings)
                        && context.UserName == UserName
                    ),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(PasswordPolicyResult.Accepted)
            .Verifiable();
    }

    private void SetupTenantIsNotServed()
    {
        MockOpenIdContextFactory
            .Setup(x =>
                x.CreateForTenantAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    It.IsAny<IMediator>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(TypedResults.NotFound().AsException("The tenant is not served."))
            .Verifiable();
    }

    private void SetupTenantContext()
    {
        var openIdContext = MockRepository.Create<OpenIdContext>();
        openIdContext
            .Protected()
            .Setup<ValueTask>("DisposeAsyncCore")
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockOpenIdContextFactory
            .Setup(x =>
                x.CreateForTenantAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    It.IsAny<IMediator>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(openIdContext.Object)
            .Verifiable();
    }

    private void SetupIdentity(Action? onLookup = null)
    {
        var identity = new PersistedFederatedIdentity
        {
            TenantId = TenantId,
            FederatedIdentityId = "identity-1",
            PrincipalId = "principal-1",
            ConnectionId = ConnectionId,
            Issuer = null,
            Subject = LocalAccountId,
            JoinKey = null,
            ConcurrencyToken = "identity-ct",
        };

        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    ConnectionId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => onLookup?.Invoke())
            .ReturnsAsync(identity)
            .Verifiable();
    }

    private void SetupNoIdentity()
    {
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    ConnectionId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((PersistedFederatedIdentity?)null)
            .Verifiable();
    }

    // The audit's values are the serialized resource; when the principal id is checked, a null expectation matches an
    // absent or null principalId.
    private static bool HasPrincipalId(JsonElement? resourceValues, string? expectedPrincipalId)
    {
        if (
            resourceValues is not { } values
            || !values.TryGetProperty("principalId", out var principalId)
            || principalId.ValueKind == JsonValueKind.Null
        )
        {
            return expectedPrincipalId is null;
        }

        return principalId.ValueKind == JsonValueKind.String
            && principalId.GetString() == expectedPrincipalId;
    }

    private void SetupAuditRecorded(
        string resourceId = LocalAccountId,
        string? expectedPrincipalId = PrincipalIdNotChecked
    )
    {
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordResourceChangeAsync(
                    It.IsAny<HttpContext>(),
                    It.Is<ResourceChangeAuditEvent>(e =>
                        e.ResourceId == resourceId
                        && (
                            expectedPrincipalId == PrincipalIdNotChecked
                            || HasPrincipalId(e.ResourceValues, expectedPrincipalId)
                        )
                    )
                )
            )
            .Callback(() => Calls.Add("audit"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    // The get route creates one store manager to read the account and resolve its principal.
    private void SetupAccountForGet(PersistedLocalAccount? account)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable(Times.Once);
        MockStoreManager
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        SetupConnection();
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

    private void SetupAuthorizationSucceeds() =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(AuthorizationResult.Success())
            .Verifiable();

    private static PersistedLocalAccount CreateAccount(
        string localAccountId = LocalAccountId,
        string connectionId = ConnectionId
    ) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = localAccountId,
            ConnectionId = connectionId,
            DisplayName = DisplayName,
            UserName = UserName,
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = "account-ct",
        };

    private static JsonPatchDocument<UpdateLocalAccountRequest> CreatePatch()
    {
        var patch = new JsonPatchDocument<UpdateLocalAccountRequest>();
        patch.Replace(x => x.UserName, UserName);
        return patch;
    }

    private void SetupUpdateValidated(string? userName, string displayName)
    {
        MockLocalAccountValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.ConnectionId == ConnectionId
                    ),
                    It.Is<PersistedLocalAccount>(account =>
                        account.LocalAccountId == LocalAccountId
                    ),
                    It.Is<UpdateLocalAccountRequest>(model =>
                        model.UserName == userName && model.DisplayName == displayName
                    ),
                    null,
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new ManagementError { StatusCode = StatusCodes.Status409Conflict, Detail = "stop" }
            )
            .Verifiable();
    }

    private static PersistedTenant CreateTenant() =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            TeamId = TeamId,
            Issuer = "https://op.example/tenant-1",
        }.Build();

    private static HttpContext CreateHttpContext()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(Mock.Of<IMediator>());
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
            RequestServices = serviceCollection.BuildServiceProvider(),
        };
    }

    private HttpContext CreateControlPlaneHttpContext(bool readsTheDefaultConnection = false)
    {
        var httpContext = CreateHttpContext();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(ControlPlaneTenantId).Verifiable();
        if (readsTheDefaultConnection)
        {
            var mockSettings = MockRepository.Create<IReadOnlySettingCollection>();
            mockSettings
                .Setup(x => x.GetValue(OpenIdSettingKeys.DefaultConnection))
                .Returns(Connection.Name)
                .Verifiable();
            var mockSettingsProvider = MockRepository.Create<IReadOnlySettingCollectionProvider>();
            mockSettingsProvider.Setup(x => x.Collection).Returns(mockSettings.Object).Verifiable();
            mockTenant
                .Setup(x => x.SettingsProvider)
                .Returns(mockSettingsProvider.Object)
                .Verifiable();
        }

        var mockContext = MockRepository.Create<OpenIdContext>();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.Setup(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();
        httpContext.Features.Set(mockFeature.Object);
        return httpContext;
    }

    private void SetupControlPlaneScope()
    {
        var mockScope = MockRepository.Create<IDisposable>();
        mockScope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(ControlPlaneTenantId))
            .Returns(mockScope.Object)
            .Verifiable();
    }

    #endregion

    #region SetEnabledAsync Tests

    [Fact]
    public async Task SetEnabledAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeUpdate(CreateForbiddenError());

        var result = await Handler.DisableLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task SetEnabledAsync_WhenDisablingAndTheTenantIsNotServed_RefusesBeforeWriting()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupIdentity();
        SetupTenantIsNotServed();
        var httpContext = CreateHttpContext();

        // No UpdateAsync/SaveChangesAsync, session or audit scaffold: a strict-mock failure would mean the account was
        // disabled, its sessions ended or the change audited although the tenant is not served.
        await Assert.ThrowsAsync<HttpResultException>(async () =>
            await Handler.DisableLocalAccountAsync(
                httpContext,
                TenantId,
                ConnectionId,
                LocalAccountId,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task SetEnabledAsync_WhenEnabling_DoesNotMaterializeTheTenant()
    {
        var account = CreateAccount();
        account.IsEnabled = false;
        SetupAuthorizeUpdate(error: null);
        SetupAccount(account);
        SetupIdentity();
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(updated => updated.IsEnabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded(expectedPrincipalId: "principal-1");
        var httpContext = CreateHttpContext();

        // No CreateForTenantAsync or session scaffold: a strict-mock failure would mean enabling an account
        // materialized the tenant or ended sessions.
        var result = await Handler.EnableLocalAccountAsync(
            httpContext,
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.True(account.IsEnabled);
    }

    [Fact]
    public async Task SetEnabledAsync_WhenDisabling_EndsTheSessionsBeforeTheAudit()
    {
        var account = CreateAccount();
        SetupAuthorizeUpdate(error: null);
        SetupAccount(account);
        SetupTenantContext();
        SetupIdentity();
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(updated => !updated.IsEnabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x =>
                x.TerminateSessionsAsync(
                    It.IsAny<OpenIdContext>(),
                    "principal-1",
                    SessionTerminationReasons.AccountDisabled
                )
            )
            .Callback(() => Calls.Add("sessions"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded(expectedPrincipalId: "principal-1");
        var httpContext = CreateHttpContext();

        var result = await Handler.DisableLocalAccountAsync(
            httpContext,
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["sessions", "audit"], Calls);
    }

    [Fact]
    public async Task SetEnabledAsync_WhenDisablingAnAccountWithoutAnIdentity_RecordsTheAuditWithoutEndingSessions()
    {
        var account = CreateAccount();
        SetupAuthorizeUpdate(error: null);
        SetupAccount(account);
        SetupNoIdentity();
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(updated => !updated.IsEnabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded(expectedPrincipalId: null);
        var httpContext = CreateHttpContext();

        var result = await Handler.DisableLocalAccountAsync(
            httpContext,
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["audit"], Calls);
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeUpdate(CreateForbiddenError());

        var result = await Handler.ResetPasswordAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            new ResetLocalAccountPasswordRequest { Password = Password },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTheTenantIsNotServed_RefusesBeforeWriting()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupPasswordAccepted();
        SetupIdentity();
        SetupTenantIsNotServed();
        var httpContext = CreateHttpContext();

        // No SetPasswordAsync/SaveChangesAsync, session, audit or attempt-limiter scaffold: a strict-mock failure would
        // mean the password was written, its sessions ended or the change audited although the tenant is not served.
        await Assert.ThrowsAsync<HttpResultException>(async () =>
            await Handler.ResetPasswordAsync(
                httpContext,
                TenantId,
                ConnectionId,
                LocalAccountId,
                new ResetLocalAccountPasswordRequest { Password = Password },
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenAccepted_EndsTheSessionsAndClearsTheCountBeforeTheAudit()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupPasswordAccepted();
        SetupTenantContext();
        SetupIdentity();
        MockLocalAccountStore
            .Setup(x =>
                x.SetPasswordAsync(
                    LocalAccountId,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x =>
                x.TerminateSessionsAsync(
                    It.IsAny<OpenIdContext>(),
                    "principal-1",
                    SessionTerminationReasons.PasswordReset
                )
            )
            .Callback(() => Calls.Add("sessions"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x => x.ResetPasswordAttemptsAsync(TenantId, ConnectionId, LocalAccountId))
            .Callback(() => Calls.Add("attempts"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded();
        var httpContext = CreateHttpContext();

        var result = await Handler.ResetPasswordAsync(
            httpContext,
            TenantId,
            ConnectionId,
            LocalAccountId,
            new ResetLocalAccountPasswordRequest { Password = Password },
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["sessions", "attempts", "audit"], Calls);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTheAccountHasNoIdentity_ClearsTheCountAndRecordsTheAudit()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupPasswordAccepted();
        SetupNoIdentity();
        MockLocalAccountStore
            .Setup(x =>
                x.SetPasswordAsync(
                    LocalAccountId,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x => x.ResetPasswordAttemptsAsync(TenantId, ConnectionId, LocalAccountId))
            .Callback(() => Calls.Add("attempts"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded();
        var httpContext = CreateHttpContext();

        var result = await Handler.ResetPasswordAsync(
            httpContext,
            TenantId,
            ConnectionId,
            LocalAccountId,
            new ResetLocalAccountPasswordRequest { Password = Password },
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["attempts", "audit"], Calls);
    }

    #endregion

    #region SetClaimsAsync Tests

    [Fact]
    public async Task SetClaimsAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeUpdate(CreateForbiddenError());

        var result = await Handler.SetClaimsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            new SetLocalAccountClaimsRequest { Claims = [] },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task SetClaimsAsync_WithANameClaim_Returns400()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());

        var result = await Handler.SetClaimsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            new SetLocalAccountClaimsRequest
            {
                Claims = [new LocalAccountClaim { Type = "name", Value = "Someone" }],
            },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "The 'name' claim is the account's display name; set displayName instead.",
            problem.ProblemDetails.Detail
        );
    }

    #endregion

    #region SetMetadataAsync Tests

    [Fact]
    public async Task SetMetadataAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeUpdate(CreateForbiddenError());

        var result = await Handler.SetMetadataAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            new SetLocalAccountMetadataRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region CreateLocalAccountAsync Tests

    [Fact]
    public async Task CreateLocalAccountAsync_WhenUnauthorized_ReturnsTheErrorWithoutOpeningTheStore()
    {
        SetupAuthorizeCreate(CreateForbiddenError());

        var result = await Handler.CreateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            new CreateLocalAccountRequest { UserName = UserName, Password = Password },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task CreateLocalAccountAsync_WhenAccepted_ClearsTheCountBeforeTheAudit()
    {
        SetupAuthorizeCreate(error: null);
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockLocalAccountValidator
            .Setup(x =>
                x.ValidateCreateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.ConnectionId == ConnectionId
                    ),
                    It.Is<PersistedLocalAccount>(account =>
                        account.UserName == UserName && account.DisplayName == UserName
                    ),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
        MockCryptoService
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns("id-1")
            .Verifiable();
        SetupPasswordAccepted();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(MockFederatedPrincipalStore.Object)
            .Verifiable();
        MockFederatedPrincipalStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedFederatedPrincipal>(), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedFederatedIdentity>(), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupConnection();
        MockLocalAccountStore
            .Setup(x =>
                x.AddAsync(
                    It.IsAny<PersistedLocalAccount>(),
                    It.IsAny<ReadOnlyMemory<byte>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (PersistedLocalAccount account, ReadOnlyMemory<byte>? _, CancellationToken _) =>
                    account.ConcurrencyToken = "account-ct"
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x => x.ResetPasswordAttemptsAsync(TenantId, ConnectionId, "id-1"))
            .Callback(() => Calls.Add("attempts"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded("id-1");
        var httpContext = CreateHttpContext();

        var result = await Handler.CreateLocalAccountAsync(
            httpContext,
            TenantId,
            ConnectionId,
            new CreateLocalAccountRequest { UserName = UserName, Password = Password },
            CancellationToken.None
        );

        var created = Assert.IsType<Created<LocalAccountResource>>(result);
        Assert.Equal("account-ct", created.Value?.ConcurrencyToken);
        Assert.Equal("id-1", created.Value?.PrincipalId);
        Assert.Equal(ConnectionId, created.Value?.ConnectionId);
        Assert.Equal("\"account-ct\"", httpContext.Response.Headers.ETag);
        Assert.Equal(["attempts", "audit"], Calls);
    }

    [Fact]
    public async Task CreateLocalAccountAsync_WhenTheConnectionIsNotLocal_Returns404()
    {
        SetupAuthorizeCreate(error: null);
        SetupStoreManagerCreated();
        SetupConnection(CreateConnection(ConnectionId, strategy: "other"));

        var result = await Handler.CreateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            new CreateLocalAccountRequest { UserName = UserName, Password = Password },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion

    #region ListLocalAccountsAsync Tests

    [Fact]
    public async Task ListLocalAccountsAsync_WhenAPageHasAccounts_ResolvesTheirPrincipalsInOneCall()
    {
        SetupAuthorizationSucceeds();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        SetupConnection();
        MockLocalAccountStore
            .Setup(x =>
                x.GetPageAsync(ConnectionId, It.IsAny<PageRequest>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new PagedResult<PersistedLocalAccount>
                {
                    Items = [CreateAccount("account-1"), CreateAccount("account-2")],
                    NextCursor = null,
                }
            )
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectsAsync(
                    ConnectionId,
                    It.Is<IReadOnlyCollection<string>>(ids =>
                        ids.SequenceEqual(new[] { "account-1", "account-2" })
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (IReadOnlyList<PersistedFederatedIdentity>)
                    [
                        new PersistedFederatedIdentity
                        {
                            TenantId = TenantId,
                            FederatedIdentityId = "identity-1",
                            PrincipalId = "principal-1",
                            ConnectionId = ConnectionId,
                            Issuer = null,
                            Subject = "account-1",
                            JoinKey = null,
                            ConcurrencyToken = "identity-ct",
                        },
                    ]
            )
            .Verifiable(Times.Once);

        var result = await Handler.ListLocalAccountsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<CollectionResource<LocalAccountResource>>>(result);
        Assert.Equal(2, json.Value?.Items.Count);
        Assert.Equal("principal-1", json.Value?.Items[0].PrincipalId);
        Assert.Null(json.Value?.Items[1].PrincipalId);
    }

    [Fact]
    public async Task ListLocalAccountsAsync_WhenTheConnectionIsNotLocal_Returns404()
    {
        SetupAuthorizationSucceeds();
        SetupStoreManagerCreated();
        SetupConnection(CreateConnection(ConnectionId, strategy: "other"));

        var result = await Handler.ListLocalAccountsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion

    #region GetLocalAccountAsync Tests

    [Fact]
    public async Task GetLocalAccountAsync_WhenTheAccountHasAnIdentity_ReturnsItsPrincipalId()
    {
        SetupAuthorizationSucceeds();
        SetupAccountForGet(CreateAccount());
        SetupIdentity();

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<LocalAccountResource>>(result);
        Assert.Equal("principal-1", json.Value?.PrincipalId);
    }

    [Fact]
    public async Task GetLocalAccountAsync_WhenTheAccountHasNoIdentity_ReturnsANullPrincipalId()
    {
        SetupAuthorizationSucceeds();
        SetupAccountForGet(CreateAccount());
        SetupNoIdentity();

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<LocalAccountResource>>(result);
        Assert.NotNull(json.Value);
        Assert.Null(json.Value.PrincipalId);
    }

    [Fact]
    public async Task GetLocalAccountAsync_ReturnsTheDisplayName()
    {
        SetupAuthorizationSucceeds();
        SetupAccountForGet(CreateAccount());
        SetupNoIdentity();

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<LocalAccountResource>>(result);
        Assert.Equal(DisplayName, json.Value?.DisplayName);
    }

    [Fact]
    public async Task GetLocalAccountAsync_WhenTheAccountBelongsToAnotherConnection_Returns404()
    {
        SetupAuthorizationSucceeds();
        SetupAccountForGet(CreateAccount(connectionId: OtherConnectionId));

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetLocalAccountAsync_WhenTheConnectionIsNotLocal_Returns404()
    {
        SetupAuthorizationSucceeds();
        SetupStoreManagerCreated();
        SetupConnection(CreateConnection(ConnectionId, strategy: "other"));

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetLocalAccountAsync_WhenTheConnectionDoesNotExist_Returns404()
    {
        SetupAuthorizationSucceeds();
        SetupStoreManagerCreated();
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedConnection?)null)
            .Verifiable();

        var result = await Handler.GetLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion

    #region UpdateLocalAccountAsync Tests

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeUpdate(CreateForbiddenError());

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CreatePatch(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenValidationFails_LooksUpNoIdentityAndWritesNothing()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        MockLocalAccountValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.ConnectionId == ConnectionId
                    ),
                    It.Is<PersistedLocalAccount>(account =>
                        account.LocalAccountId == LocalAccountId
                    ),
                    It.Is<UpdateLocalAccountRequest>(model =>
                        model.UserName == UserName && model.DisplayName == DisplayName
                    ),
                    null,
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = "Another local account has the username.",
                }
            )
            .Verifiable();

        // No identity lookup, UpdateAsync/SaveChangesAsync or audit scaffold: a strict-mock failure would mean a
        // request that failed validation looked up the identity, wrote the account or audited the change.
        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CreatePatch(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenAccepted_AuditsTheResourceWithItsPrincipalId()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        MockLocalAccountValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.ConnectionId == ConnectionId
                    ),
                    It.Is<PersistedLocalAccount>(account =>
                        account.LocalAccountId == LocalAccountId
                    ),
                    It.Is<UpdateLocalAccountRequest>(model =>
                        model.UserName == UserName && model.DisplayName == DisplayName
                    ),
                    null,
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
        SetupIdentity(onLookup: () => Calls.Add("identity"));
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(updated =>
                        updated.LocalAccountId == LocalAccountId
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Calls.Add("update"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Calls.Add("save"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAuditRecorded(expectedPrincipalId: "principal-1");

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CreatePatch(),
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["identity", "update", "save", "audit"], Calls);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_ValidatesThePatchedDisplayName()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupUpdateValidated(UserName, "Alice Liddell");
        var patch = new JsonPatchDocument<UpdateLocalAccountRequest>();
        patch.Replace(x => x.DisplayName, "  Alice Liddell ");

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            patch,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenThePatchSetsTheDisplayNameToNull_ValidatesAnEmptyDisplayName()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupUpdateValidated(UserName, string.Empty);
        var patch = new JsonPatchDocument<UpdateLocalAccountRequest>();
        patch.Replace(x => x.DisplayName, null!);

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            patch,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenThePatchRemovesTheUserName_ValidatesANullUserName()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        SetupUpdateValidated(null, DisplayName);
        var patch = new JsonPatchDocument<UpdateLocalAccountRequest>();
        patch.Remove(x => x.UserName);

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            patch,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateLocalAccountAsync_WhenThePatchCannotBeApplied_Returns400()
    {
        SetupAuthorizeUpdate(error: null);
        SetupAccount(CreateAccount());
        var patch = new JsonPatchDocument<UpdateLocalAccountRequest>();
        patch.Test(x => x.UserName, "not-the-user-name");

        var result = await Handler.UpdateLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            patch,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "The JSON Patch document could not be applied.",
            problem.ProblemDetails.Detail
        );
    }

    #endregion

    #region DeleteLocalAccountAsync Tests

    [Fact]
    public async Task DeleteLocalAccountAsync_WhenUnauthorized_ReturnsTheErrorWithoutReadingTheAccount()
    {
        SetupAuthorizeDelete(CreateForbiddenError());

        var result = await Handler.DeleteLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteLocalAccountAsync_WhenTheTenantIsNotServed_RefusesBeforeWriting()
    {
        SetupAuthorizeDelete(error: null);
        SetupAccount(CreateAccount());
        SetupDeleteValidated();
        SetupTenantIsNotServed();
        var httpContext = CreateHttpContext();

        // No identity lookup, RemoveAsync/SaveChangesAsync, session or audit scaffold: a strict-mock failure would mean
        // the account was deleted, its sessions ended or the change audited although the tenant is not served, whether
        // or not the account has a self-issued identity.
        await Assert.ThrowsAsync<HttpResultException>(async () =>
            await Handler.DeleteLocalAccountAsync(
                httpContext,
                TenantId,
                ConnectionId,
                LocalAccountId,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task DeleteLocalAccountAsync_WhenTheAccountBelongsToAnotherConnection_Returns404()
    {
        SetupAuthorizeDelete(error: null);
        SetupAccount(CreateAccount(connectionId: OtherConnectionId));

        var result = await Handler.DeleteLocalAccountAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            LocalAccountId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion

    #region Platform Tests

    [Fact]
    public async Task ListPlatformLocalAccountsAsync_ListsTheControlPlaneTenantsAccounts()
    {
        SetupControlPlaneScope();
        string? authorizedResource = null;
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Callback(
                (ClaimsPrincipal _, object? resource, IEnumerable<IAuthorizationRequirement> _) =>
                    authorizedResource = (resource as TenantScopeResource)?.TenantId
            )
            .ReturnsAsync(AuthorizationResult.Success())
            .Verifiable();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.GetByNameOrDefaultAsync(Connection.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection)
            .Verifiable();
        MockLocalAccountStore
            .Setup(x =>
                x.GetPageAsync(ConnectionId, It.IsAny<PageRequest>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new PagedResult<PersistedLocalAccount> { Items = [], NextCursor = null })
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object);
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectsAsync(
                    ConnectionId,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((IReadOnlyList<PersistedFederatedIdentity>)[]);

        var result = await Handler.ListPlatformLocalAccountsAsync(
            CreateControlPlaneHttpContext(readsTheDefaultConnection: true),
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        Assert.IsType<JsonHttpResult<CollectionResource<LocalAccountResource>>>(result);
        Assert.Equal(ControlPlaneTenantId, authorizedResource);
    }

    [Fact]
    public async Task CreatePlatformLocalAccountAsync_AuthorizesInTheControlPlaneTenant()
    {
        SetupControlPlaneScope();
        MockLocalAccountValidator
            .Setup(x =>
                x.AuthorizeCreateAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    ControlPlaneTenantId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateForbiddenError())
            .Verifiable();

        // The validator is asked about the control-plane tenant, and the strict mocks admit nothing else.
        var result = await Handler.CreatePlatformLocalAccountAsync(
            CreateControlPlaneHttpContext(),
            new CreateLocalAccountRequest { UserName = UserName, Password = Password },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData(
        "POST",
        "/connections/{connectionId}/local-accounts",
        new[] { 400, 401, 403, 404, 409, 412, 415 }
    )]
    [InlineData("GET", "/connections/{connectionId}/local-accounts", new[] { 400, 401, 403, 404 })]
    [InlineData(
        "GET",
        "/connections/{connectionId}/local-accounts/{localAccountId}",
        new[] { 401, 403, 404 }
    )]
    [InlineData(
        "PATCH",
        "/connections/{connectionId}/local-accounts/{localAccountId}",
        new[] { 400, 401, 403, 404, 409, 412, 415 }
    )]
    [InlineData(
        "DELETE",
        "/connections/{connectionId}/local-accounts/{localAccountId}",
        new[] { 401, 403, 404, 412 }
    )]
    [InlineData(
        "POST",
        "/connections/{connectionId}/local-accounts/{localAccountId}/enable",
        new[] { 401, 403, 404, 412 }
    )]
    [InlineData(
        "POST",
        "/connections/{connectionId}/local-accounts/{localAccountId}/disable",
        new[] { 401, 403, 404, 412 }
    )]
    [InlineData(
        "POST",
        "/connections/{connectionId}/local-accounts/{localAccountId}/password",
        new[] { 400, 401, 403, 404, 412, 415 }
    )]
    [InlineData(
        "GET",
        "/connections/{connectionId}/local-accounts/{localAccountId}/claims",
        new[] { 401, 403, 404 }
    )]
    [InlineData(
        "PUT",
        "/connections/{connectionId}/local-accounts/{localAccountId}/claims",
        new[] { 400, 401, 403, 404, 412, 415 }
    )]
    [InlineData(
        "GET",
        "/connections/{connectionId}/local-accounts/{localAccountId}/metadata",
        new[] { 401, 403, 404 }
    )]
    [InlineData(
        "PATCH",
        "/connections/{connectionId}/local-accounts/{localAccountId}/metadata",
        new[] { 400, 401, 403, 404, 412, 415 }
    )]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemARouteAnswers(
        string httpMethod,
        string routePattern,
        int[] statusCodes
    )
    {
        Assert.Equal(
            statusCodes,
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
    }

    #endregion
}
