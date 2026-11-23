// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Moq;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Core.Tests.Accounts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Subject;

public class DefaultValidateLocalAccountSubjectHandlerTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string LocalAccountId = "account-1";
    private const string ExternalConnectionId = "connection-ext";
    private const string ExternalIssuer = "https://idp.example";
    private const string AccountGoneMessage = "The end-user's account no longer exists.";
    private const string AccountDisabledOrGoneMessage =
        "The end-user's account is disabled or no longer exists.";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private DefaultValidateLocalAccountSubjectHandler Handler { get; }

    public DefaultValidateLocalAccountSubjectHandlerTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        Handler = new DefaultValidateLocalAccountSubjectHandler(
            MockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore)
        );
    }

    #region Scaffolding

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

    private void SetupConnection(
        string connectionId,
        string strategy = ConnectionStrategyNames.Local
    ) =>
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    connectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                LocalAccountStoreSetup.CreateConnection(
                    TenantId,
                    CreateStrictMock<IReadOnlySettingCollection>().Object,
                    strategy,
                    connectionId
                )
            )
            .Verifiable();

    private void SetupMissingConnection(string connectionId) =>
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    connectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ResolvedConnection?)null)
            .Verifiable();

    private void SetupAccount(PersistedLocalAccount? account)
    {
        SetupConnection(LocalAccountStoreSetup.ConnectionId);
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(LocalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
    }

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
            UserName = "user",
            DisplayName = "Display Name",
            Email = null,
            EmailVerified = false,
            IsEnabled = isEnabled,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    // The error factory is read only when the handler refuses the subject.
    private void SetupAccessDenied(Mock<OpenIdContext> mockContext, string description)
    {
        var mockError = CreateLooseMock<IOpenIdError>();
        mockError.SetupSet(x => x.Description = description).Verifiable();

        var mockErrorFactory = CreateLooseMock<IOpenIdErrorFactory>();
        mockErrorFactory
            .Setup(x => x.Create(OpenIdProtocol.ErrorCodes.AccessDenied))
            .Returns(mockError.Object)
            .Verifiable();

        mockContext.SetupGet(x => x.ErrorFactory).Returns(mockErrorFactory.Object).Verifiable();
    }

    private static ValidateSubjectStandingCommand CreateCommand(
        Mock<OpenIdContext> mockContext,
        OperationDisposition<IOpenIdError> disposition,
        string? connectionId = null
    )
    {
        var identity = new ClaimsIdentity("test");
        if (connectionId is not null)
        {
            identity.AddClaim(new Claim(ConnectionClaimTypes.Connection, connectionId));
        }

        var subjectAuthentication = new SubjectAuthentication(
            "scheme",
            new AuthenticationProperties(),
            new ClaimsPrincipal(identity),
            PrincipalId
        );

        return new ValidateSubjectStandingCommand(
            mockContext.Object,
            subjectAuthentication,
            disposition
        );
    }

    #endregion

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenAlreadyHasError_ShortCircuits()
    {
        var mockInitialError = CreateLooseMock<IOpenIdError>();
        var disposition = new OperationDisposition<IOpenIdError>
        {
            Error = mockInitialError.Object,
        };
        var command = CreateCommand(CreateStrictMock<OpenIdContext>(), disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Same(mockInitialError.Object, disposition.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLocalAccountIsEnabled_LeavesNoError()
    {
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: true));
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(CreateStrictMock<OpenIdContext>(), disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLocalAccountIsDisabled_SetsAccessDenied()
    {
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(Account(isEnabled: false));
        var mockContext = CreateStrictMock<OpenIdContext>();
        SetupAccessDenied(mockContext, AccountDisabledOrGoneMessage);
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(mockContext, disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLocalAccountIsGone_SetsAccessDenied()
    {
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupAccount(account: null);
        var mockContext = CreateStrictMock<OpenIdContext>();
        SetupAccessDenied(mockContext, AccountDisabledOrGoneMessage);
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(mockContext, disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenALocalSubjectHasNoIdentity_SetsAccessDenied()
    {
        SetupStoreManager();
        SetupIdentities();
        SetupConnection(LocalAccountStoreSetup.ConnectionId);
        var mockContext = CreateStrictMock<OpenIdContext>();
        SetupAccessDenied(mockContext, AccountGoneMessage);
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(mockContext, disposition, LocalAccountStoreSetup.ConnectionId);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenAnExternalSubjectHasNoIdentity_LeavesNoError()
    {
        SetupStoreManager();
        SetupIdentities();
        SetupConnection(ExternalConnectionId, strategy: "external");
        var mockContext = CreateStrictMock<OpenIdContext>();
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(mockContext, disposition, ExternalConnectionId);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenThePrincipalHasOnlyExternalIdentities_LeavesNoError()
    {
        SetupStoreManager();
        SetupIdentities(ExternalIdentity());
        SetupConnection(ExternalConnectionId, strategy: "external");
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(CreateStrictMock<OpenIdContext>(), disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenASubjectWithoutAnIdentityHasNoConnectionClaim_LeavesNoError()
    {
        SetupStoreManager();
        SetupIdentities();
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(CreateStrictMock<OpenIdContext>(), disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenTheConnectionOfAnIdentityIsGone_LeavesNoError()
    {
        SetupStoreManager();
        SetupIdentities(LocalIdentity());
        SetupMissingConnection(LocalAccountStoreSetup.ConnectionId);
        var disposition = new OperationDisposition<IOpenIdError>();
        var command = CreateCommand(CreateStrictMock<OpenIdContext>(), disposition);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    #endregion
}
