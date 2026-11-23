// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authentication;
using Moq;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Messages;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class DefaultAuthenticatePasswordGrantHandlerTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "user";
    private const string AccountKey = LocalAccountId;
    private const string Password = "password";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private Mock<IPrincipalResolver> MockPrincipalResolver { get; }
    private Mock<IAuditEventRecorder> MockAuditEventRecorder { get; }
    private Mock<IPasswordAttemptLimiter> MockPasswordAttemptLimiter { get; }
    private Mock<OpenIdContext> MockContext { get; }
    private Mock<OpenIdTenant> MockTenant { get; }
    private Mock<OpenIdClient> MockClient { get; }
    private Mock<IReadOnlySettingCollection> MockClientSettings { get; }
    private ResolvedConnection Connection { get; set; } = null!;
    private DefaultAuthenticatePasswordGrantHandler Handler { get; }

    public DefaultAuthenticatePasswordGrantHandlerTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        MockPrincipalResolver = CreateStrictMock<IPrincipalResolver>();
        MockAuditEventRecorder = CreateStrictMock<IAuditEventRecorder>();
        MockPasswordAttemptLimiter = CreateStrictMock<IPasswordAttemptLimiter>();
        MockContext = CreateStrictMock<OpenIdContext>();
        MockTenant = CreateStrictMock<OpenIdTenant>();
        MockClientSettings = CreateStrictMock<IReadOnlySettingCollection>();
        MockClient = CreateStrictMock<OpenIdClient>();
        MockClient.Setup(x => x.Settings).Returns(MockClientSettings.Object).Verifiable();
        Handler = new DefaultAuthenticatePasswordGrantHandler(
            MockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore),
            MockPrincipalResolver.Object,
            MockAuditEventRecorder.Object,
            MockPasswordAttemptLimiter.Object
        );
    }

    #region Scaffolding

    private AuthenticatePasswordGrantCommand CreateCommand(
        string userName = UserName,
        string? requestedConnection = null
    )
    {
        var mockTokenRequest = CreateStrictMock<ITokenRequest>();
        mockTokenRequest.Setup(x => x.Connection).Returns(requestedConnection).Verifiable();
        mockTokenRequest.Setup(x => x.Username).Returns(userName).Verifiable();
        mockTokenRequest.Setup(x => x.Password).Returns(Password).Verifiable();

        return new AuthenticatePasswordGrantCommand(
            MockContext.Object,
            MockClient.Object,
            mockTokenRequest.Object,
            new AuthenticationProperties()
        );
    }

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

    private void SetupSaveChanges() =>
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    // The connection's settings answer the second-factor rule, and the local_account_identifier only when the grant
    // goes on to look an account up.
    private void SetupConnection(
        bool isEmailMfaRequired,
        string requested = "",
        string strategy = ConnectionStrategyNames.Local
    )
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailMfaRequired))
            .Returns(isEmailMfaRequired);
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(LocalAccountIdentifiers.EmailOrUserName);
        Connection = LocalAccountStoreSetup.CreateConnection(
            TenantId,
            mockSettings.Object,
            strategy
        );
        SetupSelection(new ConnectionSelection { Connection = Connection }, requested);
    }

    private void SetupSelection(ConnectionSelection selection, string requested = "") =>
        MockConnectionResolver
            .Setup(x =>
                x.SelectAsync(
                    MockContext.Object,
                    MockClientSettings.Object,
                    requested == "" ? null : requested,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(selection)
            .Verifiable();

    private void SetupTenant()
    {
        MockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
        MockContext.Setup(x => x.Tenant).Returns(MockTenant.Object).Verifiable();
    }

    private IOpenIdError SetupInvalidRequest(string description)
    {
        var mockError = CreateLooseMock<IOpenIdError>();
        var mockErrorFactory = CreateLooseMock<IOpenIdErrorFactory>();
        mockErrorFactory
            .Setup(x => x.Create(OpenIdProtocol.ErrorCodes.InvalidRequest))
            .Returns(mockError.Object)
            .Verifiable();
        mockError.SetupSet(x => x.Description = description).Verifiable();
        MockContext.Setup(x => x.ErrorFactory).Returns(mockErrorFactory.Object).Verifiable();
        return mockError.Object;
    }

    private void SetupAudit(string errorCode, string errorMessage, string userName = UserName) =>
        MockAuditEventRecorder
            .Setup(x =>
                x.RecordSubjectAuthenticationFailedAsync(
                    MockContext.Object,
                    errorCode,
                    errorMessage,
                    userName,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private static PersistedLocalAccount Account(bool isEnabled) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = LocalAccountId,
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            UserName = UserName,
            DisplayName = "Display Name",
            Email = null,
            EmailVerified = false,
            IsEnabled = isEnabled,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    #endregion

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenTheSelectionIsRefused_FailsWithInvalidRequest()
    {
        var command = CreateCommand(requestedConnection: "unknown");
        SetupSelection(new ConnectionSelection { ErrorDescription = "Not allowed." }, "unknown");
        var error = SetupInvalidRequest("Not allowed.");

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
        Assert.Same(error, disposition.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheConnectionIsNotLocal_FailsWithInvalidRequest()
    {
        var command = CreateCommand(requestedConnection: "corporate");
        SetupConnection(isEmailMfaRequired: false, "corporate", strategy: "oidc");
        var error = SetupInvalidRequest(
            DefaultAuthenticatePasswordGrantHandler.ConnectionNotUsableDescription
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
        Assert.Same(error, disposition.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenNoConnectionIsSelected_FailsWithInvalidRequest()
    {
        var command = CreateCommand();
        SetupSelection(new ConnectionSelection());
        var error = SetupInvalidRequest(
            DefaultAuthenticatePasswordGrantHandler.ConnectionNotUsableDescription
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
        Assert.Same(error, disposition.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheConnectionIdentifiesByEmail_LooksUpByEmailOnly()
    {
        const string email = "user@example.com";
        var command = CreateCommand(email);
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailMfaRequired))
            .Returns(false)
            .Verifiable();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(LocalAccountIdentifiers.Email)
            .Verifiable();
        Connection = LocalAccountStoreSetup.CreateConnection(TenantId, mockSettings.Object);
        SetupSelection(new ConnectionSelection { Connection = Connection });
        SetupStoreManager();
        SetupLimit(isReached: false);
        var identifier = new LocalAccountSignInIdentifier
        {
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            Email = email,
        };
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
                    Connection,
                    AccountKey,
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsErrorCode,
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsReason,
            email
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenTheConnectionRequiresTheEmailSecondFactor_FailsWithoutVerifying()
    {
        var command = CreateCommand();
        SetupConnection(isEmailMfaRequired: true);
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.SecondFactorRequiredErrorCode,
            DefaultAuthenticatePasswordGrantHandler.SecondFactorRequiredReason
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenTheAttemptLimitIsReached_RecordsAFailureAndAuditsTheLimit()
    {
        var command = CreateCommand();
        SetupConnection(isEmailMfaRequired: false);
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account(isEnabled: true)
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
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedErrorCode,
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedReason
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenTheStoreVerifiesARefusedPassword_RecordsAFailureAndAuditsTheLimit()
    {
        var command = CreateCommand();
        SetupConnection(isEmailMfaRequired: false);
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerificationIgnoringARefusal(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account(isEnabled: true)
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
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedErrorCode,
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedReason
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenTheStoreMatchesAnotherSpelling_CountsTheStoredAccountKey()
    {
        var command = CreateCommand(UserName + " ");
        SetupConnection(isEmailMfaRequired: false);
        SetupStoreManager();
        SetupLimit(isReached: true);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName + " "),
            AccountKey,
            Account(isEnabled: true)
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
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedErrorCode,
            DefaultAuthenticatePasswordGrantHandler.AttemptLimitReachedReason,
            UserName + " "
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsWrong_RecordsAFailedAttempt()
    {
        using var cancellationSource = new CancellationTokenSource();
        var command = CreateCommand();
        SetupConnection(isEmailMfaRequired: false);
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
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsErrorCode,
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsReason
        );

        var disposition = await Handler.HandleAsync(command, cancellationSource.Token);

        Assert.False(disposition.IsAuthenticated);
    }

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsRightButTheAccountIsDisabled_ClearsTheFailedAttemptsAndFails()
    {
        var command = CreateCommand();
        SetupConnection(isEmailMfaRequired: false);
        SetupStoreManager();
        SetupLimit(isReached: false);
        LocalAccountStoreSetup.SetupVerification(
            MockLocalAccountStore,
            LocalAccountStoreSetup.EmailOrUserName(UserName),
            AccountKey,
            Account(isEnabled: false)
        );
        SetupSaveChanges();
        SetupTenant();
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
        SetupAudit(
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsErrorCode,
            DefaultAuthenticatePasswordGrantHandler.InvalidCredentialsReason
        );

        var disposition = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.IsAuthenticated);
    }

    #endregion
}
