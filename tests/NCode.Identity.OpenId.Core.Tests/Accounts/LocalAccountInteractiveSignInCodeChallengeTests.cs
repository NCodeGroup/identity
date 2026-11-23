// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountInteractiveSignInCodeChallengeTests : BaseTests
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
    private Mock<OpenIdContext> MockContext { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
    private LocalAccountInteractiveSignInCodeChallenge Challenge { get; }

    public LocalAccountInteractiveSignInCodeChallengeTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockEmailCodes = CreateStrictMock<ILocalAccountEmailCodes>();
        MockContext = CreateStrictMock<OpenIdContext>();
        MockContext.Setup(x => x.GetSubjectId(Principal)).Returns(PrincipalId).Verifiable();
        MockAuditEventRecorder = CreateStrictMock<IAuditEventRecorder>();
        Challenge = new LocalAccountInteractiveSignInCodeChallenge(
            MockStoreManagerFactory.Object,
            MockEmailCodes.Object,
            MockAuditEventRecorder.Object
        );
    }

    #region Scaffolding

    private static ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity("test"));

    private static InteractiveLocale Locale { get; } = LocalAccountMailComposerTests.CreateLocale();

    private void SetupAccount(PersistedLocalAccount account)
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
        MockFederatedIdentityStore
            .Setup(x => x.GetByPrincipalAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SelfIssuedIdentity()])
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountStore>())
            .Returns(MockLocalAccountStore.Object)
            .Verifiable();
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

    private void SetupRecordedFailure(string errorCode) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordSecondFactorFailedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode,
                    errorCode,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupRecordedSuccess() =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordSecondFactorVerifiedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupSuccessfulVerification()
    {
        SetupAccount(Account());
        SetupVerify(LocalAccountId);
        SetupRecordedSuccess();
        SetupConsume(consumed: true);
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
    }

    private void SetupConsume(bool consumed) =>
        MockEmailCodes
            .Setup(x =>
                x.ConsumeAsync(
                    MockContext.Object,
                    new EmailCodeFlow(EmailCodePurposes.SignIn, FlowId),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(consumed)
            .Verifiable();

    private void SetupVerify(string? localAccountId, string email = Email) =>
        MockEmailCodes
            .Setup(x =>
                x.VerifyAsync(
                    MockContext.Object,
                    new EmailCodeFlow(EmailCodePurposes.SignIn, FlowId),
                    It.Is<Secret<char>>(s => SecretArguments.Reveals(s, Code)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(localAccountId is null ? null : new EmailCodeMatch(localAccountId, email))
            .Verifiable();

    private static PersistedLocalAccount Account(
        string? email = Email,
        bool emailVerified = false
    ) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = LocalAccountId,
            UserName = UserName,
            DisplayName = "Display Name",
            Email = email,
            EmailVerified = emailVerified,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

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

    #endregion

    #region SendSignInCodeAsync Tests

    [Fact]
    public async Task SendSignInCodeAsync_WithoutAnAddress_RefusesNoEmail()
    {
        SetupAccount(Account(email: null));
        SetupRecordedFailure(InteractiveEmailCodeErrors.NoEmail);

        var result = await Challenge.SendSignInCodeAsync(
            MockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.NoEmail, result.Error);
    }

    [Fact]
    public async Task SendSignInCodeAsync_WithoutAnAddress_RecordsTheFailure()
    {
        SetupAccount(Account(email: null));
        SetupRecordedFailure(InteractiveEmailCodeErrors.NoEmail);

        await Challenge.SendSignInCodeAsync(
            MockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordSecondFactorFailedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode,
                    InteractiveEmailCodeErrors.NoEmail,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendSignInCodeAsync_PastTheHourlyLimit_RecordsTooManyCodes()
    {
        var account = Account();
        SetupAccount(account);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    MockContext.Object,
                    EmailCodePurposes.SignIn,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((string?)null)
            .Verifiable();
        SetupRecordedFailure(InteractiveEmailCodeErrors.TooManyCodes);

        var result = await Challenge.SendSignInCodeAsync(
            MockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.TooManyCodes, result.Error);
        MockAuditEventRecorder.Verify(
            x =>
                x.RecordSecondFactorFailedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode,
                    InteractiveEmailCodeErrors.TooManyCodes,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendSignInCodeAsync_ForAnUnverifiedAddress_SendsACode()
    {
        var account = Account();
        SetupAccount(account);
        MockEmailCodes
            .Setup(x =>
                x.SendAsync(
                    MockContext.Object,
                    EmailCodePurposes.SignIn,
                    account,
                    Locale,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(FlowId)
            .Verifiable();

        var result = await Challenge.SendSignInCodeAsync(
            MockContext.Object,
            Principal,
            Locale,
            CancellationToken.None
        );

        Assert.True(result.IsAccepted);
        Assert.Equal(FlowId, result.FlowId);
    }

    #endregion

    #region VerifySignInCodeAsync Tests

    [Fact]
    public async Task VerifySignInCodeAsync_WithAWrongCode_RefusesInvalidCode()
    {
        SetupAccount(Account());
        SetupVerify(null);
        SetupRecordedFailure(InteractiveEmailCodeErrors.InvalidCode);

        using var code = Secret.Create(Code.AsSpan());
        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
    }

    [Fact]
    public async Task VerifySignInCodeAsync_WithAWrongCode_RecordsInvalidCode()
    {
        SetupAccount(Account());
        SetupVerify(null);
        SetupRecordedFailure(InteractiveEmailCodeErrors.InvalidCode);

        using var code = Secret.Create(Code.AsSpan());
        await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordSecondFactorFailedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode,
                    InteractiveEmailCodeErrors.InvalidCode,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifySignInCodeAsync_WhenAnotherRequestTookTheCode_RefusesAndCreatesNoSession()
    {
        var account = Account();
        SetupAccount(account);
        SetupVerify(LocalAccountId);
        SetupConsume(consumed: false);
        SetupRecordedFailure(InteractiveEmailCodeErrors.InvalidCode);

        using var code = Secret.Create(Code.AsSpan());
        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
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
    public async Task VerifySignInCodeAsync_WhenTheAddressChangedSinceTheCodeWasSent_RefusesAndCreatesNoSession()
    {
        var account = Account();
        SetupAccount(account);
        SetupVerify(LocalAccountId, email: "old@example.com");
        SetupRecordedFailure(InteractiveEmailCodeErrors.InvalidCode);

        using var code = Secret.Create(Code.AsSpan());
        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
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
    public async Task VerifySignInCodeAsync_WithTheCode_MarksTheAddressVerified()
    {
        SetupSuccessfulVerification();

        using var code = Secret.Create(Code.AsSpan());
        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        Assert.Equal(InteractiveEmailCodeResult.Verified, result);
    }

    [Fact]
    public async Task VerifySignInCodeAsync_WithTheCode_RecordsTheSecondFactor()
    {
        SetupSuccessfulVerification();

        using var code = Secret.Create(Code.AsSpan());
        await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            code,
            CancellationToken.None
        );

        MockAuditEventRecorder.Verify(
            x =>
                x.RecordSecondFactorVerifiedAsync(
                    MockContext.Object,
                    PrincipalId,
                    LocalAccountId,
                    AuditCredentialFactors.EmailCode
                ),
            Times.Once
        );
        MockAuditEventRecorder.VerifyNoOtherCalls();
    }

    #endregion
}
