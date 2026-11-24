// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountInteractiveEmailVerifierTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "user";
    private const string Email = "user@example.com";
    private const string FlowId = "flow-1";
    private const string Code = "123456";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<ILocalAccountEmailCodes> MockEmailCodes { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private LocalAccountInteractiveEmailVerifier Verifier { get; }

    public LocalAccountInteractiveEmailVerifierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockEmailCodes = CreateStrictMock<ILocalAccountEmailCodes>();
        MockAuditEventRecorder = CreateStrictMock<IAuditEventRecorder>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        var catalog = LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore);
        Verifier = new LocalAccountInteractiveEmailVerifier(
            MockStoreManagerFactory.Object,
            new LocalAccountSignedInAccountFinder(
                MockStoreManagerFactory.Object,
                MockConnectionResolver.Object,
                catalog
            ),
            catalog,
            MockEmailCodes.Object,
            MockAuditEventRecorder.Object
        );
    }

    #region Scaffolding

    private static ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity("test"));

    private static InteractiveLocale Locale { get; } = LocalAccountMailComposerTests.CreateLocale();

    private Mock<OpenIdContext> CreateContext()
    {
        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.GetSubjectId(Principal)).Returns(PrincipalId).Verifiable();
        return mockContext;
    }

    private void SetupConnection(bool? isVerificationEnabled)
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        if (isVerificationEnabled is { } isEnabled)
        {
            mockSettings
                .Setup(x => x.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
                .Returns(isEnabled)
                .Verifiable();
        }

        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    LocalAccountStoreSetup.ConnectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(LocalAccountStoreSetup.CreateConnection(TenantId, mockSettings.Object))
            .Verifiable();
    }

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupAccount(PersistedLocalAccount account, bool? isVerificationEnabled = null)
    {
        SetupStoreManager();
        SetupConnection(isVerificationEnabled);
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object)
            .Verifiable();
        MockFederatedIdentityStore
            .Setup(x => x.GetByPrincipalAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([LocalIdentity()])
            .Verifiable();
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

    private void SetupRecordedFailure() =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordEmailVerificationFailedAsync(
                    It.IsAny<OpenIdContext>(),
                    PrincipalId,
                    LocalAccountId,
                    InteractiveEmailCodeErrors.InvalidCode,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupSuccessfulVerification(Mock<OpenIdContext> mockContext)
    {
        SetupAccount(Account(), isVerificationEnabled: true);
        SetupVerify(LocalAccountId);
        MockLocalAccountStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedLocalAccount>(a => a.EmailVerified),
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
            .Setup(x => x.RecordEmailVerifiedAsync(mockContext.Object, PrincipalId, LocalAccountId))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupConsume(mockContext, consumed: true);
    }

    private void SetupConsume(Mock<OpenIdContext> mockContext, bool consumed) =>
        MockEmailCodes
            .Setup(x =>
                x.ConsumeAsync(
                    mockContext.Object,
                    new EmailCodeFlow(EmailCodePurposes.VerifyEmail, FlowId),
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
                    new EmailCodeFlow(EmailCodePurposes.VerifyEmail, FlowId),
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
        string? email = Email,
        bool emailVerified = false
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
            IsEnabled = true,
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

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_WithoutAnAddress_ReturnsNull()
    {
        var mockContext = CreateContext();
        SetupAccount(Account(email: null));

        var status = await Verifier.GetStatusAsync(
            mockContext.Object,
            Principal,
            CancellationToken.None
        );

        Assert.Null(status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStatusAsync_WithAnAddress_ReportsWhetherItIsVerified(bool isVerified)
    {
        var mockContext = CreateContext();
        SetupAccount(Account(emailVerified: isVerified));

        var status = await Verifier.GetStatusAsync(
            mockContext.Object,
            Principal,
            CancellationToken.None
        );

        Assert.NotNull(status);
        Assert.Equal(Email, status.Email);
        Assert.Equal(isVerified, status.IsVerified);
    }

    #endregion

    #region SendVerificationCodeAsync Tests

    [Fact]
    public async Task SendVerificationCodeAsync_WhenDisabled_RefusesNotSupported()
    {
        var mockContext = CreateContext();
        SetupAccount(Account(), isVerificationEnabled: false);

        var result = await Verifier.SendVerificationCodeAsync(
            mockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.NotSupported, result.Error);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_ForAVerifiedAddress_RefusesAlreadyVerified()
    {
        var mockContext = CreateContext();
        SetupAccount(Account(emailVerified: true), isVerificationEnabled: true);

        var result = await Verifier.SendVerificationCodeAsync(
            mockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.AlreadyVerified, result.Error);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_PastTheHourlyLimit_RefusesTooManyCodes()
    {
        var mockContext = CreateContext();
        var account = Account();
        SetupAccount(account, isVerificationEnabled: true);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    mockContext.Object,
                    EmailCodePurposes.VerifyEmail,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((string?)null)
            .Verifiable();

        var result = await Verifier.SendVerificationCodeAsync(
            mockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.TooManyCodes, result.Error);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_ForAnUnverifiedAddress_SendsACode()
    {
        var mockContext = CreateContext();
        var account = Account();
        SetupAccount(account, isVerificationEnabled: true);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    mockContext.Object,
                    EmailCodePurposes.VerifyEmail,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(FlowId)
            .Verifiable();

        var result = await Verifier.SendVerificationCodeAsync(
            mockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.True(result.IsAccepted);
        Assert.Equal(FlowId, result.FlowId);
    }

    #endregion

    #region VerifyEmailAsync Tests

    [Fact]
    public async Task VerifyEmailAsync_WithAnotherAccountsFlow_RefusesInvalidCode()
    {
        var mockContext = CreateContext();
        SetupAccount(Account(), isVerificationEnabled: true);
        SetupVerify("account-2");
        SetupRecordedFailure();

        using var code = Secret.Create(Code.AsSpan());
        var result = await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
    }

    [Fact]
    public async Task VerifyEmailAsync_WithAnotherAccountsFlow_RecordsInvalidCode()
    {
        var mockContext = CreateContext();
        SetupAccount(Account(), isVerificationEnabled: true);
        SetupVerify("account-2");
        SetupRecordedFailure();

        using var code = Secret.Create(Code.AsSpan());
        await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordEmailVerificationFailedAsync(
                    mockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    InteractiveEmailCodeErrors.InvalidCode,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenAnotherRequestTookTheCode_RefusesAndVerifiesNothing()
    {
        var mockContext = CreateContext();
        var account = Account();
        SetupAccount(account, isVerificationEnabled: true);
        SetupVerify(LocalAccountId);
        SetupConsume(mockContext, consumed: false);
        SetupRecordedFailure();

        using var code = Secret.Create(Code.AsSpan());
        var result = await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
        Assert.False(account.EmailVerified);
        MockLocalAccountStore.Verify(
            x => x.UpdateAsync(It.IsAny<PersistedLocalAccount>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenTheAddressChangedSinceTheCodeWasSent_RefusesAndVerifiesNothing()
    {
        var mockContext = CreateContext();
        var account = Account();
        SetupAccount(account, isVerificationEnabled: true);
        SetupVerify(LocalAccountId, email: "old@example.com");
        SetupRecordedFailure();

        using var code = Secret.Create(Code.AsSpan());
        var result = await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
        Assert.False(account.EmailVerified);
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
            x => x.UpdateAsync(It.IsAny<PersistedLocalAccount>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task VerifyEmailAsync_WithTheCode_MarksTheAddressVerified()
    {
        var mockContext = CreateContext();
        SetupSuccessfulVerification(mockContext);

        using var code = Secret.Create(Code.AsSpan());
        var result = await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.Equal(InteractiveEmailCodeResult.Verified, result);
    }

    [Fact]
    public async Task VerifyEmailAsync_WithTheCode_RecordsTheVerification()
    {
        var mockContext = CreateContext();
        SetupSuccessfulVerification(mockContext);

        using var code = Secret.Create(Code.AsSpan());
        await Verifier.VerifyEmailAsync(
            mockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x => x.RecordEmailVerifiedAsync(mockContext.Object, PrincipalId, LocalAccountId),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    #endregion
}
