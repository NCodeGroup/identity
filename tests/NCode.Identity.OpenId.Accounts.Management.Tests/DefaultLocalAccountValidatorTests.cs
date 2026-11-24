// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class DefaultLocalAccountValidatorTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string LocalAccountId = "account-1";
    private const string TeamId = "team-1";
    private const string ConnectionId = "connection-1";
    private const string OtherConnectionId = "connection-2";
    private const string UserName = "alice";
    private const string Email = "alice@example.com";
    private const string DisplayName = "Alice";
    private const string ConcurrencyToken = "account-ct";

    private MockRepository MockRepository { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private PersistedConnection Connection { get; }
    private DefaultLocalAccountValidator Validator { get; }

    public DefaultLocalAccountValidatorTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockLocalAccountStore = MockRepository.Create<ILocalAccountStore>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        var mockPasswordPolicy = MockRepository.Create<IPasswordPolicy>();

        Connection = CreateConnection(ConnectionId);
        var catalog = new LocalAccountStoreCatalog([
            new LocalAccountStoreRegistration
            {
                Name = "default",
                CreateStore = _ => MockLocalAccountStore.Object,
                CreateTransferStore = _ =>
                    MockRepository.Create<ILocalAccountTransferStore>().Object,
            },
        ]);

        Validator = new DefaultLocalAccountValidator(
            MockAuthorizationService.Object,
            new LocalAccountPasswords(
                MockEffectiveSettingsResolver.Object,
                mockPasswordPolicy.Object
            ),
            catalog
        );
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private static ClaimsPrincipal CreateUser(bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "test")
            : new ClaimsIdentity();
        return new ClaimsPrincipal(identity);
    }

    private static PersistedConnection CreateConnection(string connectionId) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            Name = connectionId,
            Strategy = ConnectionStrategyNames.Local,
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Value = JsonSerializer.SerializeToElement(new { }),
            },
        };

    private static PersistedLocalAccount CreateAccount(
        string localAccountId = LocalAccountId,
        string? userName = UserName,
        string? email = null,
        string displayName = DisplayName,
        string connectionId = ConnectionId
    ) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = localAccountId,
            ConnectionId = connectionId,
            UserName = userName,
            DisplayName = displayName,
            Email = email,
            EmailVerified = false,
            SecurityStamp = "stamp",
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = ConcurrencyToken,
        };

    private static UpdateLocalAccountRequest CreateModel(
        string? userName = UserName,
        string? email = null,
        string displayName = DisplayName
    ) =>
        new()
        {
            UserName = userName,
            Email = email,
            DisplayName = displayName,
        };

    private void SetupAuthorization(AuthorizationResult result)
    {
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();
    }

    private Func<object?> SetupAuthorizationCapturingTheResource(
        IAuthorizationRequirement requirement
    )
    {
        object? authorizedResource = null;
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(r => r.Contains(requirement))
                )
            )
            .Callback(
                (ClaimsPrincipal _, object? resource, IEnumerable<IAuthorizationRequirement> _) =>
                    authorizedResource = resource
            )
            .ReturnsAsync(AuthorizationResult.Failed())
            .Verifiable();
        return () => authorizedResource;
    }

    private void SetupIdentifiers(string mode, PersistedConnection? connection = null)
    {
        connection ??= Connection;
        var tenant = new PersistedTenantBuilder { TenantId = TenantId, TeamId = TeamId }.Build();
        var teamSettings = Mock.Of<IReadOnlySettingCollection>();
        var tenantSettings = Mock.Of<IReadOnlySettingCollection>();
        var connectionSettings = MockRepository.Create<IReadOnlySettingCollection>();
        connectionSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.LocalAccountIdentifier))
            .Returns(mode)
            .Verifiable();

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
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(tenantSettings)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    tenantSettings,
                    OpenIdSettingScopes.Connection,
                    connection.ConnectionId,
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(connectionSettings.Object)
            .Verifiable();
    }

    private void SetupUserNameLookup(
        PersistedLocalAccount? existing,
        string userName = UserName,
        string connectionId = ConnectionId
    )
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(connectionId, userName, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(existing)
            .Verifiable();

        // The user name is also looked up as another account's email address; no account has it unless a test says so.
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(connectionId, userName, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((PersistedLocalAccount?)null);
    }

    private void SetupEmailLookup(
        PersistedLocalAccount? existing,
        string email = Email,
        string connectionId = ConnectionId
    )
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(connectionId, email, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(existing)
            .Verifiable();

        // The email address is also looked up as another account's user name; no account has it unless a test says so.
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(connectionId, email, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((PersistedLocalAccount?)null);
    }

    #endregion

    #region AuthorizeCreateAsync Tests

    [Fact]
    public async Task AuthorizeCreateAsync_WhenAuthorized_ReturnsNull()
    {
        SetupAuthorization(AuthorizationResult.Success());

        var error = await Validator.AuthorizeCreateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task AuthorizeCreateAsync_WhenForbiddenAndAuthenticated_Returns403()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeCreateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeCreateAsync_WhenForbiddenAndAnonymous_Returns401()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeCreateAsync(
            CreateUser(authenticated: false),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status401Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeCreateAsync_AuthorizesCreateOnTheTenantScope()
    {
        var authorizedResource = SetupAuthorizationCapturingTheResource(Operations.Create);

        await Validator.AuthorizeCreateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        var scope = Assert.IsType<TenantScopeResource>(authorizedResource());
        Assert.Equal(TenantId, scope.TenantId);
    }

    #endregion
    #region AuthorizeUpdateAsync Tests

    [Fact]
    public async Task AuthorizeUpdateAsync_WhenAuthorized_ReturnsNull()
    {
        SetupAuthorization(AuthorizationResult.Success());

        var error = await Validator.AuthorizeUpdateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task AuthorizeUpdateAsync_WhenForbiddenAndAuthenticated_Returns403()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeUpdateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeUpdateAsync_WhenForbiddenAndAnonymous_Returns401()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeUpdateAsync(
            CreateUser(authenticated: false),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status401Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeUpdateAsync_AuthorizesUpdateOnTheTenantScope()
    {
        var authorizedResource = SetupAuthorizationCapturingTheResource(Operations.Update);

        await Validator.AuthorizeUpdateAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        var scope = Assert.IsType<TenantScopeResource>(authorizedResource());
        Assert.Equal(TenantId, scope.TenantId);
    }

    #endregion
    #region AuthorizeDeleteAsync Tests

    [Fact]
    public async Task AuthorizeDeleteAsync_WhenAuthorized_ReturnsNull()
    {
        SetupAuthorization(AuthorizationResult.Success());

        var error = await Validator.AuthorizeDeleteAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task AuthorizeDeleteAsync_WhenForbiddenAndAuthenticated_Returns403()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeDeleteAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeDeleteAsync_WhenForbiddenAndAnonymous_Returns401()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var error = await Validator.AuthorizeDeleteAsync(
            CreateUser(authenticated: false),
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status401Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task AuthorizeDeleteAsync_AuthorizesDeleteOnTheTenantScope()
    {
        var authorizedResource = SetupAuthorizationCapturingTheResource(Operations.Delete);

        await Validator.AuthorizeDeleteAsync(
            CreateUser(authenticated: true),
            TenantId,
            CancellationToken.None
        );

        var scope = Assert.IsType<TenantScopeResource>(authorizedResource());
        Assert.Equal(TenantId, scope.TenantId);
    }

    #endregion
    #region ValidateCreateAsync Tests

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameTaken_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(CreateAccount());
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal("A local account with the specified username already exists.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenEmailTakenInTheConnection_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        SetupEmailLookup(CreateAccount(localAccountId: "other-account", email: Email));
        var account = CreateAccount(email: Email);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal(
            "A local account with the specified email address already exists.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheUserNameIsAnotherAccountsEmail_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(ConnectionId, UserName, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateAccount(localAccountId: "other-account", userName: "bob"))
            .Verifiable();
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal(
            "The specified username is the email address of another local account.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIsAnotherAccountsUserName_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        SetupEmailLookup(existing: null);
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(ConnectionId, Email, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateAccount(localAccountId: "other-account", userName: Email))
            .Verifiable();
        var account = CreateAccount(email: Email);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal(
            "The specified email address is the username of another local account.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIsOnlyTakenInAnotherConnection_ReturnsNull()
    {
        var otherConnection = CreateConnection(OtherConnectionId);
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName, otherConnection);
        SetupUserNameLookup(existing: null, connectionId: OtherConnectionId);
        SetupEmailLookup(existing: null, connectionId: OtherConnectionId);
        var account = CreateAccount(email: Email, connectionId: OtherConnectionId);

        var error = await Validator.ValidateCreateAsync(
            otherConnection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenValid_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIdentifierIsRequiredAndEmailIsMissing_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.Email);
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The email address is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIdentifierIsRequiredAndEmailIsPresent_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.Email);
        SetupEmailLookup(existing: null);
        var account = CreateAccount(userName: null, email: Email);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheUserNameIdentifierIsRequiredAndUserNameIsMissing_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.UserName);
        var account = CreateAccount(userName: null, email: Email);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The username is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenNeitherIdentifierIsGiven_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var account = CreateAccount(userName: "   ");

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("A username or an email address is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIsTakenAndTheDisplayNameIsBlank_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var account = CreateAccount(email: Email, displayName: "   ");

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The display name is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenTheEmailIsTakenAndTheDisplayNameIsValid_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        SetupEmailLookup(CreateAccount(localAccountId: "other-account", email: Email));
        var account = CreateAccount(email: Email);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenDisplayNameIsBlank_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var account = CreateAccount(displayName: "   ");

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The display name is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenDisplayNameIsTooLong_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var displayName = new string('a', AccountMaxLengths.DisplayName + 1);
        var account = CreateAccount(displayName: displayName);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal(
            $"The display name must be at most {AccountMaxLengths.DisplayName} characters.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenDisplayNameIsAtTheMaximumLength_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        var displayName = new string('a', AccountMaxLengths.DisplayName);
        var account = CreateAccount(displayName: displayName);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameIsTooLong_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var userName = new string('a', AccountMaxLengths.UserName + 1);
        var account = CreateAccount(userName: userName);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal(
            $"The username must be at most {AccountMaxLengths.UserName} characters.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameIsAtTheMaximumLength_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var userName = new string('a', AccountMaxLengths.UserName);
        SetupUserNameLookup(existing: null, userName: userName);
        var account = CreateAccount(userName: userName);

        var error = await Validator.ValidateCreateAsync(
            Connection,
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    #endregion

    #region ValidateUpdateAsync Tests

    [Fact]
    public async Task ValidateUpdateAsync_WhenIfMatchMismatch_Returns412()
    {
        var account = CreateAccount();
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            "stale-ct",
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenIfMatchIsTheQuotedEntityTag_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        var account = CreateAccount(userName: "old-name");
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            "\"account-ct\"",
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameTakenByAnotherAccount_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(CreateAccount(localAccountId: "other-account"));
        var account = CreateAccount(userName: "old-name");
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            ConcurrencyToken,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenEmailTakenByAnotherAccountInTheConnection_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        SetupEmailLookup(CreateAccount(localAccountId: "other-account", email: Email));
        var account = CreateAccount();
        var model = CreateModel(email: Email);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenTheUserNameIsAnotherAccountsEmail_Returns409()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(ConnectionId, UserName, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateAccount(localAccountId: "other-account", userName: "bob"))
            .Verifiable();
        var account = CreateAccount(userName: "old-name");
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenTheUserNameEqualsTheAccountsOwnEmail_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var own = CreateAccount(userName: "old-name", email: Email);
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(ConnectionId, Email, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(own);
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(ConnectionId, Email, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(own);
        var model = CreateModel(userName: Email, email: Email);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            own,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameUnchangedOnSameAccount_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(CreateAccount(localAccountId: LocalAccountId));
        var account = CreateAccount(userName: "old-name");
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenNoIdentifierChangesOnAnAccountThatBreaksTheMode_ReturnsNull()
    {
        var account = CreateAccount(email: null);
        var model = CreateModel(displayName: "Alice Liddell");

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
        MockLocalAccountStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenThePatchClearsTheLastRequiredIdentifier_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var account = CreateAccount();
        var model = CreateModel(userName: null);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenDisplayNameIsBlank_Returns400()
    {
        var account = CreateAccount();
        var model = CreateModel(displayName: string.Empty);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The display name is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenDisplayNameIsTooLong_Returns400()
    {
        var account = CreateAccount();
        var displayName = new string('a', AccountMaxLengths.DisplayName + 1);
        var model = CreateModel(displayName: displayName);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal(
            $"The display name must be at most {AccountMaxLengths.DisplayName} characters.",
            error.Detail
        );
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenDisplayNameIsAtTheMaximumLength_ReturnsNull()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        SetupUserNameLookup(existing: null);
        var account = CreateAccount(userName: "old-name");
        var displayName = new string('a', AccountMaxLengths.DisplayName);
        var model = CreateModel(displayName: displayName);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenTheUserNameIdentifierIsRequiredAndUserNameIsMissing_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.UserName);
        var account = CreateAccount();
        var model = CreateModel(userName: null);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The username is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenTheEmailIdentifierIsRequiredAndEmailIsMissing_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.Email);
        var account = CreateAccount(email: Email);
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The email address is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameIsTooLong_Returns400()
    {
        SetupIdentifiers(LocalAccountIdentifiers.EmailOrUserName);
        var account = CreateAccount();
        var userName = new string('a', AccountMaxLengths.UserName + 1);
        var model = CreateModel(userName: userName);

        var error = await Validator.ValidateUpdateAsync(
            Connection,
            account,
            model,
            null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal(
            $"The username must be at most {AccountMaxLengths.UserName} characters.",
            error.Detail
        );
    }

    #endregion

    #region ValidateDeleteAsync Tests

    [Fact]
    public async Task ValidateDeleteAsync_ReturnsNull()
    {
        var account = CreateAccount();

        var error = await Validator.ValidateDeleteAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    #endregion
}
