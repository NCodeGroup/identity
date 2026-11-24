// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountInteractiveCredentialVerifierTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "user";
    private const string AccountKey = LocalAccountId;
    private const string Password = "password";

    private static DateTimeOffset Now { get; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IAuthenticationContextReferenceResolver> MockResolver { get; }
    private FakeTimeProvider TimeProvider { get; } = new(Now);
    private Mock<IPasswordAttemptLimiter> MockPasswordAttemptLimiter { get; }
    private Mock<OpenIdContext> MockContext { get; }
    private Mock<IReadOnlySettingCollection> MockSettings { get; }
    private ResolvedConnection Connection { get; }
    private LocalAccountInteractiveCredentialVerifier Verifier { get; }

    public LocalAccountInteractiveCredentialVerifierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockResolver = CreateStrictMock<IAuthenticationContextReferenceResolver>();
        MockPasswordAttemptLimiter = CreateStrictMock<IPasswordAttemptLimiter>();
        MockContext = CreateStrictMock<OpenIdContext>();
        MockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(LocalAccountIdentifiers.EmailOrUserName);
        Connection = LocalAccountStoreSetup.CreateConnection(TenantId, MockSettings.Object);
        var subjectFactory = new LocalAccountSubjectFactory(MockResolver.Object, TimeProvider);
        Verifier = new LocalAccountInteractiveCredentialVerifier(
            MockStoreManagerFactory.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore),
            subjectFactory,
            MockPasswordAttemptLimiter.Object
        );
    }

    #region Scaffolding

    private void SetupLimit(bool isReached) =>
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.IsLimitReachedAsync(
                    MockContext.Object,
                    Connection,
                    AccountKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(isReached)
            .Verifiable();

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupPrincipalBuild()
    {
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.SubjectClaimTypes))
            .Returns((IReadOnlyCollection<string>)["sub"])
            .Verifiable();

        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        MockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();

        MockResolver
            .Setup(x =>
                x.ResolveAsync(
                    It.IsAny<AuthenticationContextReferenceRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((string?)null)
            .Verifiable();
    }

    private static PersistedLocalAccount Account() =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = LocalAccountId,
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            UserName = UserName,
            DisplayName = "Display Name",
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    #endregion

    #region VerifyCredentialAsync Tests

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheConnectionIsNotLocal_ReturnsNullWithoutReadingTheStore()
    {
        using var password = Secret.Create(Password.AsSpan());
        var connection = LocalAccountStoreSetup.CreateConnection(
            TenantId,
            MockSettings.Object,
            strategy: "custom"
        );

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            connection,
            UserName,
            password,
            CancellationToken.None
        );

        Assert.Null(principal);
        MockStoreManagerFactory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheAttemptLimitIsReached_RecordsAFailureAndReturnsNull()
    {
        using var password = Secret.Create(Password.AsSpan());
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account()
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    MockContext.Object,
                    Connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            Connection,
            UserName,
            password,
            CancellationToken.None
        );

        Assert.Null(principal);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheStoreVerifiesARefusedPassword_RecordsAFailureAndReturnsNull()
    {
        using var password = Secret.Create(Password.AsSpan());
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerificationIgnoringARefusal(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account()
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    MockContext.Object,
                    Connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            Connection,
            UserName,
            password,
            CancellationToken.None
        );

        Assert.Null(principal);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheStoreDoesNotAskTheGate_Throws()
    {
        using var password = Secret.Create(Password.AsSpan());
        SetupStoreManager();
        LocalAccountStoreSetup.SetupVerificationWithoutTheGate(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account()
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Verifier.VerifyCredentialAsync(
                MockContext.Object,
                Connection,
                UserName,
                password,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheStoreMatchesAnotherSpelling_CountsTheStoredAccountKey()
    {
        using var password = Secret.Create(Password.AsSpan());
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName + " "),
            AccountKey,
            Account()
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    MockContext.Object,
                    Connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            Connection,
            UserName + " ",
            password,
            CancellationToken.None
        );

        Assert.Null(principal);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenThePasswordIsWrong_RecordsAFailedAttempt()
    {
        using var password = Secret.Create(Password.AsSpan());
        using var cancellationSource = new CancellationTokenSource();
        SetupStoreManager();
        SetupLimit(isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            verified: null
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    MockContext.Object,
                    Connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            Connection,
            UserName,
            password,
            cancellationSource.Token
        );

        Assert.Null(principal);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenThePasswordIsRight_ClearsTheFailedAttempts()
    {
        using var password = Secret.Create(Password.AsSpan());
        SetupStoreManager();
        SetupLimit(isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account()
        );
        SetupPrincipalBuild();
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.ResetAsync(
                    TenantId,
                    LocalAccountStoreSetup.ConnectionId,
                    AccountKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            Connection,
            UserName,
            password,
            CancellationToken.None
        );

        Assert.NotNull(principal);
        Assert.Equal(LocalAccountId, principal.FindFirstValue("sub"));
        var name = principal.FindFirstValue(JoseClaimNames.Payload.Name);
        Assert.Equal("Display Name", name);
        Assert.Equal(
            LocalAccountStoreSetup.ConnectionId,
            principal.FindFirstValue(ConnectionClaimTypes.Connection)
        );
        Assert.Equal(
            LocalAccountStoreSetup.ConnectionName,
            principal.FindFirstValue(JoseClaimNames.Payload.Idp)
        );
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheConnectionIdentifiesByEmail_LooksUpByEmailOnly()
    {
        using var password = Secret.Create(Password.AsSpan());
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(LocalAccountIdentifiers.Email)
            .Verifiable();
        var connection = LocalAccountStoreSetup.CreateConnection(TenantId, mockSettings.Object);
        var identifier = new LocalAccountSignInIdentifier
        {
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            Email = "user@example.com",
        };
        SetupStoreManager();
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.IsLimitReachedAsync(
                    MockContext.Object,
                    connection,
                    AccountKey,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            identifier,
            AccountKey,
            verified: null
        );
        MockPasswordAttemptLimiter
            .Setup(x =>
                x.RecordFailureAsync(
                    MockContext.Object,
                    connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var principal = await Verifier.VerifyCredentialAsync(
            MockContext.Object,
            connection,
            "user@example.com",
            password,
            CancellationToken.None
        );

        Assert.Null(principal);
    }

    #endregion
}
