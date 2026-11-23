// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
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

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IPasswordPolicy> MockPasswordPolicy { get; }
    private Mock<IPasswordAttemptLimiter> MockPasswordAttemptLimiter { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
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
        Changer = new LocalAccountInteractivePasswordChanger(
            MockStoreManagerFactory.Object,
            MockPasswordPolicy.Object,
            MockPasswordAttemptLimiter.Object,
            MockAuditEventRecorder.Object
        );
    }

    #region Scaffolding

    private static ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity("test"));

    private Mock<OpenIdTenant> SetupPolicy(
        Mock<OpenIdContext> mockContext,
        PasswordPolicyResult result
    )
    {
        var mockSettings = CreateLooseMock<IReadOnlySettingCollection>();
        var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
        mockProvider.Setup(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();

        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c => c.UserName == UserName),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<PasswordPolicyContext, ReadOnlyMemory<char>, CancellationToken>(
                (_, password, _) => CapturedNewPassword = password.ToString()
            )
            .ReturnsAsync(result)
            .Verifiable();

        return mockTenant;
    }

    private void SetupLimit(Mock<OpenIdContext> mockContext, bool isReached) =>
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.IsLimitReachedAsync(mockContext.Object, UserName, It.IsAny<CancellationToken>())
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

    private void SetupAccount(PersistedLocalAccount? account)
    {
        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountStore>())
            .Returns(MockLocalAccountStore.Object)
            .Verifiable();
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

    private static PersistedFederatedIdentity SelfIssuedIdentity() =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "fed-1",
            PrincipalId = PrincipalId,
            Issuer = AccountConstants.SelfIssuer,
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
            Issuer = ExternalIssuer,
            Subject = "external-subject",
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedLocalAccount Account(bool isEnabled) =>
        new()
        {
            TenantId = TenantId,
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
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        var mockTenant = SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            UserName,
            UserName,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.ResetAsync(TenantId, UserName, It.IsAny<CancellationToken>()))
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
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            UserName,
            UserName,
            verified: null
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.RecordFailureAsync(mockContext.Object, UserName, CancellationToken.None))
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
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            UserName,
            UserName,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.RecordFailureAsync(mockContext.Object, UserName, CancellationToken.None))
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
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: true);
        LocalAccountStoreSetup.SetupVerificationIgnoringARefusal(
            MockLocalAccountStore,
            UserName,
            UserName,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.RecordFailureAsync(mockContext.Object, UserName, CancellationToken.None))
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
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(
            mockContext,
            PasswordPolicyResult.Rejected(PasswordPolicyErrors.TooShort, "too short")
        );

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
        SetupIdentities(SelfIssuedIdentity());
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
    public async Task ChangePasswordAsync_WithTheCurrentPassword_RecordsTheChange()
    {
        using var cancellationSource = new CancellationTokenSource();
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        var mockTenant = SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            UserName,
            UserName,
            Account(isEnabled: true)
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.ResetAsync(TenantId, UserName, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupPasswordChange();
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordChangedAsync(mockContext.Object, PrincipalId, LocalAccountId)
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

        Assert.Equal(InteractivePasswordChangeResult.Changed, result);
        MockAuditEventRecorder.Verify(
            x => x.RecordPasswordChangedAsync(mockContext.Object, PrincipalId, LocalAccountId),
            Times.Once
        );
    }

    [Fact]
    public async Task ChangePasswordAsync_WithAWrongCurrentPassword_RecordsTheFailure()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(mockContext, PasswordPolicyResult.Accepted);
        SetupLimit(mockContext, isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            UserName,
            UserName,
            verified: null
        );
        MockPasswordAttemptLimiter
            .Setup(x => x.RecordFailureAsync(mockContext.Object, UserName, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupFailureRecorded(
            mockContext,
            InteractivePasswordChangeErrors.InvalidCurrentPassword,
            CancellationToken.None
        );

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
        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordChangeFailedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    InteractivePasswordChangeErrors.InvalidCurrentPassword,
                    CancellationToken.None
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenThePolicyRefuses_RecordsTheFailureWithThePolicysCode()
    {
        var mockContext = CreateContext();
        SetupStoreManager();
        SetupIdentities(SelfIssuedIdentity());
        SetupAccount(Account(isEnabled: true));
        SetupPolicy(
            mockContext,
            PasswordPolicyResult.Rejected(PasswordPolicyErrors.TooShort, "too short")
        );
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
