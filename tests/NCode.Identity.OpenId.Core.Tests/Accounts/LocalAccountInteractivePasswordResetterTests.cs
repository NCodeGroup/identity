// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountInteractivePasswordResetterTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "user";
    private const string Email = "user@example.com";
    private const string FlowId = "flow-1";
    private const string HandleOfNoFlow = "handle-of-no-flow";
    private const string DecoyFlowId = "decoy-flow-1";
    private const string Code = "123456";
    private const string NewPassword = "new";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IPasswordPolicy> MockPasswordPolicy { get; }
    private Mock<ILocalAccountEmailCodes> MockEmailCodes { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IPostCommitSecurityEffects> MockPostCommitSecurityEffects { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private Mock<IReadOnlySettingCollection> MockConnectionSettings { get; }
    private ResolvedConnection Connection { get; }
    private LocalAccountInteractivePasswordResetter Resetter { get; }

    public LocalAccountInteractivePasswordResetterTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockPasswordPolicy = CreateStrictMock<IPasswordPolicy>();
        MockEmailCodes = CreateStrictMock<ILocalAccountEmailCodes>();
        MockCryptoService = CreateStrictMock<ICryptoService>();
        MockPostCommitSecurityEffects = CreateStrictMock<IPostCommitSecurityEffects>();
        MockAuditEventRecorder = CreateStrictMock<IAuditEventRecorder>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        MockConnectionSettings = CreateStrictMock<IReadOnlySettingCollection>();
        Connection = LocalAccountStoreSetup.CreateConnection(
            TenantId,
            MockConnectionSettings.Object
        );
        Resetter = new LocalAccountInteractivePasswordResetter(
            MockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore),
            MockPasswordPolicy.Object,
            MockEmailCodes.Object,
            MockCryptoService.Object,
            MockPostCommitSecurityEffects.Object,
            MockAuditEventRecorder.Object
        );
    }

    #region Scaffolding

    private static InteractiveLocale Locale { get; } = LocalAccountMailComposerTests.CreateLocale();

    private Mock<OpenIdContext> CreateContext(
        bool isResetEnabled,
        bool needsTenantId = false,
        Mock<IReadOnlySettingCollection>? mockSettings = null
    )
    {
        // The connection's settings decide whether a reset is allowed and which identifier signs a user in; a store
        // lookup reads the identifier only when the submitted value names no account.
        MockConnectionSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
            .Returns(isResetEnabled);
        MockConnectionSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(LocalAccountIdentifiers.EmailOrUserName);

        var mockContext = CreateStrictMock<OpenIdContext>();
        if (!needsTenantId && mockSettings is null)
        {
            return mockContext;
        }

        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        if (mockSettings is not null)
        {
            var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
            mockProvider.Setup(x => x.Collection).Returns(mockSettings.Object).Verifiable();
            mockTenant.Setup(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        }

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        return mockContext;
    }

    private void SetupConnection() =>
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    LocalAccountStoreSetup.ConnectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Connection)
            .Verifiable();

    private void SetupHandle() =>
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(HandleOfNoFlow)
            .Verifiable();

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    // A submitted value that names no user name is looked up as an email address.
    private void SetupLookupByUserName(PersistedLocalAccount? account)
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(
                    LocalAccountStoreSetup.ConnectionId,
                    UserName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(account)
            .Verifiable();
        if (account is null)
        {
            MockLocalAccountStore
                .Setup(x =>
                    x.GetByEmailOrDefaultAsync(
                        LocalAccountStoreSetup.ConnectionId,
                        UserName,
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync((PersistedLocalAccount?)null)
                .Verifiable();
        }
    }

    private void SetupLookupById(PersistedLocalAccount? account) =>
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();

    private void SetupFederatedIdentity()
    {
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    LocalAccountStoreSetup.ConnectionId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(LocalIdentity())
            .Verifiable();
    }

    private void SetupNoPrincipalForRandomIdentifier()
    {
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    LocalAccountStoreSetup.ConnectionId,
                    HandleOfNoFlow,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((PersistedFederatedIdentity?)null)
            .Verifiable();
    }

    private void SetupDecoy(Mock<OpenIdContext> mockContext) =>
        MockEmailCodes
            .Setup(x =>
                x.SendDecoyAsync(
                    mockContext.Object,
                    EmailCodePurposes.ResetPassword,
                    UserName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(DecoyFlowId)
            .Verifiable();

    private void SetupAddDecoyFlow(Mock<OpenIdContext> mockContext) =>
        MockEmailCodes
            .Setup(x =>
                x.AddDecoyFlowAsync(
                    mockContext.Object,
                    EmailCodePurposes.ResetPassword,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(DecoyFlowId)
            .Verifiable();

    private void SetupRequested(Mock<OpenIdContext> mockContext) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordResetRequestedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupRequestFailed(Mock<OpenIdContext> mockContext, string errorCode) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordResetRequestFailedAsync(
                    mockContext.Object,
                    errorCode,
                    UserName,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupResetFailed(
        Mock<OpenIdContext> mockContext,
        string? subjectId,
        string? localAccountId,
        string errorCode
    ) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordPasswordResetFailedAsync(
                    mockContext.Object,
                    subjectId,
                    localAccountId,
                    errorCode,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupSuccessfulReset(Mock<OpenIdContext> mockContext, List<string>? calls = null)
    {
        SetupVerify(LocalAccountId);
        SetupConnection();
        SetupStoreManager();
        SetupLookupById(Account());
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c => c.UserName == UserName),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(PasswordPolicyResult.Accepted)
            .Verifiable();
        SetupFederatedIdentity();
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
        MockAuditEventRecorder
            .Setup(x => x.RecordPasswordResetAsync(mockContext.Object, PrincipalId, LocalAccountId))
            .Callback(() => calls?.Add(nameof(IAuditEventRecorder.RecordPasswordResetAsync)))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupConsume(mockContext, consumed: true);
        MockPostCommitSecurityEffects
            .Setup(x =>
                x.TerminateSessionsAsync(
                    mockContext.Object,
                    PrincipalId,
                    SessionTerminationReasons.PasswordReset
                )
            )
            .Callback(() => calls?.Add(nameof(IPostCommitSecurityEffects.TerminateSessionsAsync)))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockPostCommitSecurityEffects
            .Setup(x =>
                x.ResetPasswordAttemptsAsync(
                    TenantId,
                    LocalAccountStoreSetup.ConnectionId,
                    LocalAccountId
                )
            )
            .Callback(() =>
                calls?.Add(nameof(IPostCommitSecurityEffects.ResetPasswordAttemptsAsync))
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private void SetupConsume(Mock<OpenIdContext> mockContext, bool consumed) =>
        MockEmailCodes
            .Setup(x =>
                x.ConsumeAsync(
                    mockContext.Object,
                    new EmailCodeFlow(EmailCodePurposes.ResetPassword, FlowId),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(consumed)
            .Verifiable();

    private void SetupVerify(string? localAccountId, string email = Email) =>
        MockEmailCodes
            .Setup(x =>
                x.VerifyAsync(
                    It.IsAny<OpenIdContext>(),
                    new EmailCodeFlow(EmailCodePurposes.ResetPassword, FlowId),
                    It.Is<Secret<char>>(s => SecretArguments.Reveals(s, Code)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                localAccountId is null
                    ? null
                    : new EmailCodeMatch(LocalAccountStoreSetup.ConnectionId, localAccountId, email)
            )
            .Verifiable();

    private static PersistedLocalAccount Account(
        bool isEnabled = true,
        string? email = Email,
        bool emailVerified = true
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            LocalAccountId = LocalAccountId,
            UserName = UserName,
            DisplayName = "Display Name",
            Email = email,
            EmailVerified = emailVerified,
            IsEnabled = isEnabled,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

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

    #endregion

    #region SendResetCodeAsync Tests

    [Fact]
    public async Task SendResetCodeAsync_WhenResetIsDisabled_ReturnsAHandleAndSendsNothing()
    {
        var mockContext = CreateContext(isResetEnabled: false);
        SetupHandle();

        var flowId = await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(HandleOfNoFlow, flowId);
    }

    [Fact]
    public async Task SendResetCodeAsync_WhenResetIsDisabled_RecordsNothing()
    {
        var mockContext = CreateContext(isResetEnabled: false);
        SetupHandle();

        await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendResetCodeAsync_ForAnUnknownUserName_ReadsAPrincipalAndSendsADecoy()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupStoreManager();
        SetupLookupByUserName(account: null);
        SetupHandle();
        SetupNoPrincipalForRandomIdentifier();
        SetupDecoy(mockContext);
        SetupRequestFailed(mockContext, "account_not_eligible");

        var flowId = await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(DecoyFlowId, flowId);
        MockFederatedIdentityStore.Verify(
            x =>
                x.GetByConnectionSubjectAsync(
                    LocalAccountStoreSetup.ConnectionId,
                    HandleOfNoFlow,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task SendResetCodeAsync_ForAnUnknownUserName_RecordsTheFailureWithTheUserName()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupStoreManager();
        SetupLookupByUserName(account: null);
        SetupHandle();
        SetupNoPrincipalForRandomIdentifier();
        SetupDecoy(mockContext);
        SetupRequestFailed(mockContext, "account_not_eligible");

        await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordResetRequestFailedAsync(
                    mockContext.Object,
                    "account_not_eligible",
                    UserName,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendResetCodeAsync_ForAnIneligibleUserName_SendsADecoyAndNoMail()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupStoreManager();
        SetupLookupByUserName(Account(emailVerified: false));
        SetupFederatedIdentity();
        SetupDecoy(mockContext);
        SetupRequestFailed(mockContext, "account_not_eligible");

        var flowId = await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(DecoyFlowId, flowId);
        MockEmailCodes.Verify(
            x =>
                x.SendAsync(
                    It.IsAny<OpenIdContext>(),
                    It.IsAny<string>(),
                    It.IsAny<PersistedLocalAccount>(),
                    It.IsAny<InteractiveLocale>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task SendResetCodeAsync_PastTheHourlyLimit_RecordsTheFailureWithTheUserName()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        var account = Account();
        SetupStoreManager();
        SetupLookupByUserName(account);
        SetupFederatedIdentity();
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    mockContext.Object,
                    EmailCodePurposes.ResetPassword,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((string?)null)
            .Verifiable();
        SetupAddDecoyFlow(mockContext);
        SetupRequestFailed(mockContext, InteractiveEmailCodeErrors.TooManyCodes);

        var flowId = await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(DecoyFlowId, flowId);
        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordResetRequestFailedAsync(
                    mockContext.Object,
                    InteractiveEmailCodeErrors.TooManyCodes,
                    UserName,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendResetCodeAsync_WhenTheAccountIsOverItsSendLimit_AddsADecoyFlowWithoutCountingAgain()
    {
        const int sendsPerHour = 5;
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailCodeSendsPerHour))
            .Returns(sendsPerHour)
            .Verifiable();
        var mockContext = CreateContext(
            isResetEnabled: true,
            needsTenantId: true,
            mockSettings: mockSettings
        );
        var mockValueSet = CreateStrictMock<IExpiringValueSet>();
        var mockCounterSet = CreateStrictMock<IExpiringCounterSet>();
        var mockMailSender = CreateStrictMock<IMailSender>();
        var emailCodes = new DefaultLocalAccountEmailCodes(
            mockValueSet.Object,
            mockCounterSet.Object,
            mockMailSender.Object,
            MockCryptoService.Object,
            TimeProvider.System
        );
        var resetter = new LocalAccountInteractivePasswordResetter(
            MockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore),
            MockPasswordPolicy.Object,
            emailCodes,
            MockCryptoService.Object,
            MockPostCommitSecurityEffects.Object,
            MockAuditEventRecorder.Object
        );
        SetupStoreManager();
        SetupLookupByUserName(Account());
        SetupFederatedIdentity();
        MockCryptoService
            .Setup(x => x.GenerateKey(32, BinaryEncodings.Base64Url))
            .Returns(DecoyFlowId)
            .Verifiable();
        var sendsKey = ExpiringKeys.Create(
            DefaultLocalAccountEmailCodes.SendsKeyPurpose,
            TenantId,
            EmailCodePurposes.ResetPassword,
            LocalAccountId
        );
        mockCounterSet
            .Setup(x =>
                x.IncrementAsync(
                    sendsKey,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(sendsPerHour + 1)
            .Verifiable();
        var decoyFlowKey = DefaultLocalAccountEmailCodes.CreateFlowKey(
            TenantId,
            EmailCodePurposes.ResetPassword,
            DecoyFlowId
        );
        mockValueSet
            .Setup(x =>
                x.TryAddAsync(
                    decoyFlowKey,
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        SetupRequestFailed(mockContext, InteractiveEmailCodeErrors.TooManyCodes);

        var flowId = await resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(DecoyFlowId, flowId);
        mockCounterSet.Verify(
            x =>
                x.IncrementAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        mockValueSet.Verify(
            x =>
                x.TryAddAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        mockValueSet.VerifyNoOtherCalls();
        mockCounterSet.VerifyNoOtherCalls();
        mockMailSender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendResetCodeAsync_ForAVerifiedAddress_SendsACode()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        var account = Account();
        SetupStoreManager();
        SetupLookupByUserName(account);
        SetupFederatedIdentity();
        SetupRequested(mockContext);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    mockContext.Object,
                    EmailCodePurposes.ResetPassword,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(FlowId)
            .Verifiable();

        var flowId = await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        Assert.Equal(FlowId, flowId);
    }

    [Fact]
    public async Task SendResetCodeAsync_ForAVerifiedAddress_RecordsTheRequest()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        var account = Account();
        SetupStoreManager();
        SetupLookupByUserName(account);
        SetupFederatedIdentity();
        SetupRequested(mockContext);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    mockContext.Object,
                    EmailCodePurposes.ResetPassword,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(FlowId)
            .Verifiable();

        await Resetter.SendResetCodeAsync(
            mockContext.Object,
            Connection,
            UserName,
            Locale,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordResetRequestedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_WhenResetIsDisabled_ReturnsNotSupported()
    {
        var mockContext = CreateContext(isResetEnabled: false);
        SetupVerify(LocalAccountId);
        SetupConnection();

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.NotSupported, result);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAWrongCode_RefusesWithInvalidCode()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(localAccountId: null);
        SetupResetFailed(
            mockContext,
            subjectId: null,
            localAccountId: null,
            InteractiveEmailCodeErrors.InvalidCode
        );

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAWrongCode_RecordsInvalidCode()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(localAccountId: null);
        SetupResetFailed(
            mockContext,
            subjectId: null,
            localAccountId: null,
            InteractiveEmailCodeErrors.InvalidCode
        );

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordResetFailedAsync(
                    mockContext.Object,
                    null,
                    null,
                    InteractiveEmailCodeErrors.InvalidCode,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenThePolicyRefuses_KeepsTheFlowAndRefusesWithThePolicyError()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(LocalAccountId);
        SetupConnection();
        SetupStoreManager();
        SetupLookupById(Account());
        SetupFederatedIdentity();
        SetupResetFailed(mockContext, PrincipalId, LocalAccountId, PasswordPolicyErrors.TooShort);
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c => c.UserName == UserName && c.Email == Email),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(PasswordPolicyResult.Rejected(PasswordPolicyErrors.TooShort, "too short"))
            .Verifiable();

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(PasswordPolicyErrors.TooShort, result.Error);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenAnotherRequestTookTheCode_RefusesAndChangesNothing()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(LocalAccountId);
        SetupConnection();
        SetupStoreManager();
        SetupLookupById(Account());
        SetupFederatedIdentity();
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c => c.UserName == UserName),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(PasswordPolicyResult.Accepted)
            .Verifiable();
        SetupConsume(mockContext, consumed: false);
        SetupResetFailed(
            mockContext,
            PrincipalId,
            LocalAccountId,
            InteractiveEmailCodeErrors.InvalidCode
        );

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
        MockLocalAccountStore.Verify(
            x =>
                x.SetPasswordAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTheAddressChangedSinceTheCodeWasSent_RefusesAndChangesNothing()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(LocalAccountId, email: "old@example.com");
        SetupConnection();
        SetupStoreManager();
        SetupLookupById(Account());
        SetupResetFailed(
            mockContext,
            subjectId: null,
            localAccountId: null,
            InteractiveEmailCodeErrors.InvalidCode
        );

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
        MockEmailCodes.Verify(
            x =>
                x.ConsumeAsync(
                    It.IsAny<OpenIdContext>(),
                    It.IsAny<EmailCodeFlow>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockLocalAccountStore.Verify(
            x =>
                x.SetPasswordAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTheAccountIsDisabled_RefusesAndChangesNothing()
    {
        var mockContext = CreateContext(isResetEnabled: true);
        SetupVerify(LocalAccountId);
        SetupConnection();
        SetupStoreManager();
        SetupLookupById(Account(isEnabled: false));
        SetupResetFailed(
            mockContext,
            subjectId: null,
            localAccountId: null,
            InteractiveEmailCodeErrors.InvalidCode
        );

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.False(result.IsChanged);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
        MockAuditEventRecorder.Verify(
            x =>
                x.RecordPasswordResetFailedAsync(
                    mockContext.Object,
                    null,
                    null,
                    InteractiveEmailCodeErrors.InvalidCode,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockLocalAccountStore.Verify(
            x =>
                x.SetPasswordAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResetPasswordAsync_WithTheCode_SetsThePasswordEndsTheSessionsAndClearsTheAttempts()
    {
        var mockContext = CreateContext(isResetEnabled: true, needsTenantId: true);
        SetupSuccessfulReset(mockContext);

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        var result = await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(InteractivePasswordChangeResult.Changed, result);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithTheCode_RecordsTheReset()
    {
        var mockContext = CreateContext(isResetEnabled: true, needsTenantId: true);
        SetupSuccessfulReset(mockContext);

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x => x.RecordPasswordResetAsync(mockContext.Object, PrincipalId, LocalAccountId),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResetPasswordAsync_Always_RecordsTheResetAfterTheSecurityEffects()
    {
        var calls = new List<string>();
        var mockContext = CreateContext(isResetEnabled: true, needsTenantId: true);
        SetupSuccessfulReset(mockContext, calls);

        using var code = Secret.Create(Code.AsSpan());
        using var newPassword = Secret.Create(NewPassword.AsSpan());
        await Resetter.ResetPasswordAsync(
            mockContext.Object,
            FlowId,
            code,
            newPassword,
            CancellationToken.None
        );

        Assert.Equal(
            [
                nameof(IPostCommitSecurityEffects.TerminateSessionsAsync),
                nameof(IPostCommitSecurityEffects.ResetPasswordAttemptsAsync),
                nameof(IAuditEventRecorder.RecordPasswordResetAsync),
            ],
            calls
        );
    }

    #endregion
}
