// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class DefaultLocalAccountValidatorTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string LocalAccountId = "account-1";
    private const string UserName = "alice";
    private const string DisplayName = "Alice";
    private const string ConcurrencyToken = "account-ct";

    private MockRepository MockRepository { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private DefaultLocalAccountValidator Validator { get; }

    public DefaultLocalAccountValidatorTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockLocalAccountStore = MockRepository.Create<ILocalAccountStore>();

        Validator = new DefaultLocalAccountValidator(MockAuthorizationService.Object);
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

    private static PersistedLocalAccount CreateAccount(
        string localAccountId = LocalAccountId,
        string userName = UserName,
        string displayName = DisplayName
    ) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = displayName,
            Email = null,
            EmailVerified = false,
            SecurityStamp = "stamp",
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = ConcurrencyToken,
        };

    private static UpdateLocalAccountRequest CreateModel(
        string userName = UserName,
        string displayName = DisplayName
    ) => new() { UserName = userName, DisplayName = displayName };

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

    private void SetupUserNameLookup(PersistedLocalAccount? existing, string userName = UserName)
    {
        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountStore>())
            .Returns(MockLocalAccountStore.Object)
            .Verifiable();
        MockLocalAccountStore
            .Setup(x => x.GetByUserNameOrDefaultAsync(userName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing)
            .Verifiable();
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
        SetupUserNameLookup(CreateAccount());
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenValid_ReturnsNull()
    {
        SetupUserNameLookup(existing: null);
        var account = CreateAccount();

        var error = await Validator.ValidateCreateAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenDisplayNameIsBlank_Returns400()
    {
        var account = CreateAccount(displayName: "   ");

        var error = await Validator.ValidateCreateAsync(
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
        var displayName = new string('a', AccountMaxLengths.DisplayName + 1);
        var account = CreateAccount(displayName: displayName);

        var error = await Validator.ValidateCreateAsync(
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
        SetupUserNameLookup(existing: null);
        var displayName = new string('a', AccountMaxLengths.DisplayName);
        var account = CreateAccount(displayName: displayName);

        var error = await Validator.ValidateCreateAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameIsNull_Returns400()
    {
        var account = CreateAccount(userName: null!);

        var error = await Validator.ValidateCreateAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The username is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameIsBlank_Returns400()
    {
        var account = CreateAccount(userName: "   ");

        var error = await Validator.ValidateCreateAsync(
            account,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The username is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenUserNameIsTooLong_Returns400()
    {
        var userName = new string('a', AccountMaxLengths.UserName + 1);
        var account = CreateAccount(userName: userName);

        var error = await Validator.ValidateCreateAsync(
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
        var userName = new string('a', AccountMaxLengths.UserName);
        SetupUserNameLookup(existing: null, userName);
        var account = CreateAccount(userName: userName);

        var error = await Validator.ValidateCreateAsync(
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
            account,
            model,
            ifMatch: "stale-ct",
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenIfMatchIsTheQuotedEntityTag_ReturnsNull()
    {
        SetupUserNameLookup(existing: null);
        var account = CreateAccount();
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: "\"account-ct\"",
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameTakenByAnotherAccount_Returns409()
    {
        SetupUserNameLookup(CreateAccount(localAccountId: "other-account"));
        var account = CreateAccount();
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: ConcurrencyToken,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameUnchangedOnSameAccount_ReturnsNull()
    {
        SetupUserNameLookup(CreateAccount(localAccountId: LocalAccountId));
        var account = CreateAccount();
        var model = CreateModel();

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenDisplayNameIsBlank_Returns400()
    {
        var account = CreateAccount();
        var model = CreateModel(displayName: string.Empty);

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: null,
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
            account,
            model,
            ifMatch: null,
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
        SetupUserNameLookup(existing: null);
        var account = CreateAccount();
        var displayName = new string('a', AccountMaxLengths.DisplayName);
        var model = CreateModel(displayName: displayName);

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameIsNull_Returns400()
    {
        var account = CreateAccount();
        var model = CreateModel(userName: null!);

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: null,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(error);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Equal("The username is required.", error.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenUserNameIsTooLong_Returns400()
    {
        var account = CreateAccount();
        var userName = new string('a', AccountMaxLengths.UserName + 1);
        var model = CreateModel(userName: userName);

        var error = await Validator.ValidateUpdateAsync(
            account,
            model,
            ifMatch: null,
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
