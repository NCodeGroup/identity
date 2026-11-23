// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountInteractivePasswordChangerTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "user";
    private const string CurrentPassword = "current";
    private const string NewPassword = "new";
    private const string ExternalIssuer = "https://idp.example";
    private const string ExternalConnectionId = "connection-ext";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IPasswordPolicy> MockPasswordPolicy { get; }
    private Mock<IPasswordAttemptLimiter> MockPasswordAttemptLimiter { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private ResolvedConnection Connection { get; }
    private LocalAccountInteractivePasswordChanger Changer { get; }

    private string? CapturedNewPassword { get; set; }

    public LocalAccountInteractivePasswordChangerTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockPasswordPolicy = CreateStrictMock<IPasswordPolicy>();
        MockPasswordAttemptLimiter = CreateStrictMock<IPasswordAttemptLimiter>();
        MockAuditEventRecorder = CreateStrictMock<IAuditEventRecorder>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        Connection = LocalAccountStoreSetup.CreateConnection(
            TenantId,
            CreateLooseMock<IReadOnlySettingCollection>().Object
        );
        Changer = new LocalAccountInteractivePasswordChanger(
            MockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore),
            MockPasswordPolicy.Object,
            MockPasswordAttemptLimiter.Object,
            MockAuditEventRecorder.Object
        );
    }

    #region Scaffolding

    private static ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity("test"));

    private static LocalAccountSignInIdentifier Identifier { get; } =
        new() { ConnectionId = LocalAccountStoreSetup.ConnectionId, UserName = UserName };

    private void SetupPolicy(PasswordPolicyResult result) =>
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c =>
                        c.UserName == UserName && ReferenceEquals(c.Settings, Connection.Settings)
                    ),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<PasswordPolicyContext, ReadOnlyMemory<char>, CancellationToken>(
                (_, password, _) => CapturedNewPassword = password.ToString()
            )
            .ReturnsAsync(result)
            .Verifiable();

    private void SetupTenantId(Mock<OpenIdContext> mockContext)
    {
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
    }

    private void SetupLimit(Mock<OpenIdContext> mockContext, bool isReached) =>
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.IsLimitReachedAsync(
                    mockContext.Object,
                    Connection,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(isReached)
            .Verifiable();

    private Mock<OpenIdContext> CreateContext()
    {
        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.GetSubjectId(Principal)).Returns(PrincipalId).Verifiable();
        return mockContext;
    }

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
    }

    private void SetupIdentities(params PersistedFederatedIdentity[] identities) =>
        MockFederatedIdentityStore
            .Setup(x => x.GetByPrincipalAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities)
            .Verifiable();

    private void SetupConnection(string connectionId, ResolvedConnection connection) =>
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    connectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(connection)
            .Verifiable();

    private void SetupExternalConnection() =>
        SetupConnection(
            ExternalConnectionId,
            LocalAccountStoreSetup.CreateConnection(
                TenantId,
                CreateLooseMock<IReadOnlySettingCollection>().Object,
                strategy: "external",
                connectionId: ExternalConnectionId
            )
        );

    private void SetupAccount(PersistedLocalAccount? account)
    {
        SetupConnection(LocalAccountStoreSetup.ConnectionId, Connection);
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

    private void SetupPasswordChange()
    {
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
    }

    private void SetupChangeRecorded(Mock<OpenIdContext> mockContext) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordChangedAsync(mockContext.Object, PrincipalId, LocalAccountId)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupFailureRecorded(
        Mock<OpenIdContext> mockContext,
        string errorCode,
        CancellationToken cancellationToken
    ) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordChangeFailedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    errorCode,
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private static PersistedFederatedIdentity LocalIdentity() =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "fed-1",
            PrincipalId = PrincipalId,
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            Issuer = null,
            Subject = LocalAccountId,
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedFederatedIdentity ExternalIdentity() =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "fed-ext",
            PrincipalId = PrincipalId,
            ConnectionId = ExternalConnectionId,
            Issuer = ExternalIssuer,
            Subject = "external-subject",
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedLocalAccount Account(bool isEnabled) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            LocalAccountId = LocalAccountId,
            UserName = UserName,
            DisplayName = "Display Name",
            Email = null,
            EmailVerified = false,
            IsEnabled = isEnabled,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    #endregion

    #region ChangePasswordAsync Tests

    [Fact]
    public async Task ChangePasswordAsync_WhenTheCurrentPasswordIsValid_SetsTheNewPassword()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Accepted);
        SetupTenantId(mockContext);
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            Identifier,
            LocalAccountId,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.ResetAsync(
                    TenantId,
                    LocalAccountStoreSetup.ConnectionId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupPasswordChange();
        SetupChangeRecorded(mockContext);
        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.Changed, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTheCurrentPasswordIsInvalid_ChangesNothing()
    {
        using var cancellationSource = new CancellationTokenSource();
        var mockContext = CreateContext();
        SetupFailureRecorded(
            mockContext,
            InteractivePasswordChangeErrors.InvalidCurrentPassword,
            CancellationToken.None
        );
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            Identifier,
            LocalAccountId,
            verified: null
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    mockContext.Object,
                    Connection,
                    LocalAccountId,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            cancellationSource.Token
        );

        Assert.Equal(InteractivePasswordChangeResult.CurrentPasswordInvalid, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTheAttemptLimitIsReached_ReturnsCurrentPasswordInvalidAndRecordsAFailure()
    {
        var mockContext = CreateContext();
        SetupFailureRecorded(
            mockContext,
            InteractivePasswordChangeErrors.InvalidCurrentPassword,
            CancellationToken.None
        );
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            Identifier,
            LocalAccountId,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    mockContext.Object,
                    Connection,
                    LocalAccountId,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.CurrentPasswordInvalid, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTheStoreVerifiesARefusedPassword_ReturnsCurrentPasswordInvalidAndRecordsAFailure()
    {
        var mockContext = CreateContext();
        SetupFailureRecorded(
            mockContext,
            InteractivePasswordChangeErrors.InvalidCurrentPassword,
            CancellationToken.None
        );
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: true);
        LocalAccountStoreSetup.SetupVerificationIgnoringARefusal(
            MockLocalAccountStore,
            Identifier,
            LocalAccountId,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    mockContext.Object,
                    Connection,
                    LocalAccountId,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.CurrentPasswordInvalid, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenThePolicyRefusesTheNewPassword_ReturnsItsErrorWithoutVerifying()
    {
        var mockContext = CreateContext();
        SetupFailureRecorded(mockContext, PasswordPolicyErrors.TooShort, CancellationToken.None);
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Rejected(PasswordPolicyErrors.TooShort, "too short"));

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(PasswordPolicyErrors.TooShort, result.Error);
        Assert.Equal(NewPassword, CapturedNewPassword);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenThePrincipalHasNoLocalAccount_ReturnsNotSupported()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(ExternalIdentity());
        SetupExternalConnection();

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.NotSupported, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTheAccountIsDisabled_ReturnsNotSupported()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: false));

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.NotSupported, result);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenThePolicyRefuses_RecordsTheFailureWithThePolicysCode()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(PasswordPolicyResult.Rejected(PasswordPolicyErrors.TooShort, "too short"));
        SetupFailureRecorded(mockContext, PasswordPolicyErrors.TooShort, CancellationToken.None);

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(PasswordPolicyErrors.TooShort, result.Error);
        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordChangeFailedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    PasswordPolicyErrors.TooShort,
                    CancellationToken.None
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task ChangePasswordAsync_WithoutAnAccount_RecordsNothing()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(ExternalIdentity());
        SetupExternalConnection();

        using var currentPassword = Secret.Create(CurrentPassword.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Changer.ChangePasswordAsync(
            mockContext.Object,
            Principal,
            currentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.NotSupported, result);
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    #endregion
}
