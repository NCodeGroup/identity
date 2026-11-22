// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Identity.OpenId.Accounts;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
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
        Challenge = new LocalAccountInteractiveSignInCodeChallenge(
            MockStoreManagerFactory.Object,
            MockEmailCodes.Object
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

    private void SetupVerify(string? localAccountId) =>
        MockEmailCodes
            .Setup(x =>
                x.VerifyAsync(
                    MockContext.Object,
                    EmailCodePurposes.SignIn,
                    FlowId,
                    Code,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(localAccountId)
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

        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.False(result.IsAccepted);
        Assert.Equal(InteractiveEmailCodeErrors.InvalidCode, result.Error);
    }

    [Fact]
    public async Task VerifySignInCodeAsync_WithTheCode_MarksTheAddressVerified()
    {
        var account = Account();
        SetupAccount(account);
        SetupVerify(LocalAccountId);
        MockEmailCodes
            .Setup(x =>
                x.CompleteAsync(
                    MockContext.Object,
                    EmailCodePurposes.SignIn,
                    FlowId,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
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

        var result = await Challenge.VerifySignInCodeAsync(
            MockContext.Object,
            Principal,
            FlowId,
            Code,
            CancellationToken.None
        );

        Assert.Equal(InteractiveEmailCodeResult.Verified, result);
        Assert.True(account.EmailVerified);
    }

    #endregion
}
