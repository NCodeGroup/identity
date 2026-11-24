// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;
using SystemTextJsonPatch;
using SystemTextJsonPatch.Exceptions;

namespace NCode.Identity.OpenId.Accounts.Management;

/*

POST   api/tenants/{tenantId}/connections/{connectionId}/local-accounts
GET    api/tenants/{tenantId}/connections/{connectionId}/local-accounts
GET    api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}
PATCH  api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}
DELETE api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}

POST   api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/enable
POST   api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/disable
POST   api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/password

GET    api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/claims
PUT    api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/claims

GET    api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/metadata
PATCH  api/tenants/{tenantId}/connections/{connectionId}/local-accounts/{localAccountId}/metadata

POST   api/platform/local-accounts
GET    api/platform/local-accounts

The platform routes operate on the local connection that the control-plane tenant's default_connection setting names.

*/

/// <summary>
/// Provides the management endpoints for local accounts, which are nested under the local connection that holds them.
/// Local accounts are end-user subjects, not ownable resources, so every operation authorizes against the owning tenant
/// scope (there is no per-account owner model). A route loads its connection, answers 404 when the connection is
/// missing or is not a local connection, and operates on the <see cref="ILocalAccountStore"/> that the connection's
/// options select from the <see cref="LocalAccountStoreCatalog"/>; an account that another connection holds is answered
/// 404. Deleting or disabling an account, or resetting its password, ends every session of its principal. Every
/// password it sets meets the tenant's password policy; a refused password is answered 400 with the requirement it
/// fails. The security effects that follow a commit (ending the sessions of a disabled account or of a reset password,
/// and clearing failed password attempts) run through <see cref="IPostCommitSecurityEffects"/>.
/// </summary>
internal class LocalAccountApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    ILocalAccountValidator localAccountValidator,
    ILocalAccountCreator localAccountCreator,
    IManagementAuditRecorder managementAuditRecorder,
    ISessionService sessionService,
    IOpenIdContextFactory openIdContextFactory,
    LocalAccountPasswords localAccountPasswords,
    IPostCommitSecurityEffects postCommitSecurityEffects,
    IAmbientTenantAccessor ambientTenantAccessor,
    LocalAccountStoreCatalog localAccountStoreCatalog
) : ManagementApiEndpointHandler, IEndpointProvider
{
    private const string PrincipalRoleRemovalReason = "principal role removal";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    private ILocalAccountValidator LocalAccountValidator { get; } = localAccountValidator;
    private ILocalAccountCreator LocalAccountCreator { get; } = localAccountCreator;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private ISessionService SessionService { get; } = sessionService;
    private IOpenIdContextFactory OpenIdContextFactory { get; } = openIdContextFactory;
    private LocalAccountPasswords LocalAccountPasswords { get; } = localAccountPasswords;
    private IPostCommitSecurityEffects PostCommitSecurityEffects { get; } =
        postCommitSecurityEffects;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var accounts = endpoints
            .MapGroup("/connections/{connectionId}/local-accounts")
            .WithTags(OpenIdConstants.EndpointTags.LocalAccounts);

        accounts
            .MapPost("", CreateLocalAccountAsync)
            .Produces<LocalAccountResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        accounts
            .MapGet("", ListLocalAccountsAsync)
            .Produces<CollectionResource<LocalAccountResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        accounts
            .MapGet("/{localAccountId}", GetLocalAccountAsync)
            .Produces<LocalAccountResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        accounts
            .MapPatch("/{localAccountId}", UpdateLocalAccountAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        accounts
            .MapDelete("/{localAccountId}", DeleteLocalAccountAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        accounts
            .MapPost("/{localAccountId}/enable", EnableLocalAccountAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
        accounts
            .MapPost("/{localAccountId}/disable", DisableLocalAccountAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
        accounts
            .MapPost("/{localAccountId}/password", ResetPasswordAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        accounts
            .MapGet("/{localAccountId}/claims", GetClaimsAsync)
            .Produces<LocalAccountClaimsResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        accounts
            .MapPut("/{localAccountId}/claims", SetClaimsAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        accounts
            .MapGet("/{localAccountId}/metadata", GetMetadataAsync)
            .Produces<LocalAccountMetadataResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        accounts
            .MapPatch("/{localAccountId}/metadata", SetMetadataAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    internal virtual LocalAccountResource ToLocalAccountResource(
        PersistedLocalAccount account,
        string? principalId
    ) =>
        new()
        {
            TenantId = account.TenantId,
            LocalAccountId = account.LocalAccountId,
            PrincipalId = principalId,
            DisplayName = account.DisplayName,
            UserName = account.UserName,
            Email = account.Email,
            EmailVerified = account.EmailVerified,
            IsEnabled = account.IsEnabled,
            ConnectionId = account.ConnectionId,
            ConcurrencyToken = account.ConcurrencyToken,
        };

    /// <summary>
    /// Creates a local account in a local connection, together with the principal and the identity that it resolves to.
    /// </summary>
    /// <response code="201">The created local account.</response>
    /// <response code="400">The identifier the connection requires is missing or longer than allowed, the display name is longer than allowed, the claims include the reserved <c>name</c> claim, or the password does not meet the connection's password policy.</response>
    /// <response code="404">The tenant does not exist, or the connection does not exist or is not a local connection.</response>
    /// <response code="409">A local account with the username or the email address already exists in the connection.</response>
    internal virtual ValueTask<IResult> CreateLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromBody] CreateLocalAccountRequest request,
        CancellationToken cancellationToken
    ) => CreateCoreAsync(httpContext, tenantId, connectionId, request, cancellationToken);

    private async ValueTask<IResult> CreateCoreAsync(
        HttpContext httpContext,
        string tenantId,
        string? connectionId,
        CreateLocalAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeCreateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var connection = await FindConnectionAsync(
            httpContext,
            storeManager,
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var creation = await LocalAccountCreator.StageAsync(
            storeManager,
            tenantId,
            connection,
            request,
            cancellationToken
        );
        if (!creation.Succeeded)
        {
            return ToErrorResult(creation.Error);
        }

        var account = creation.Account;
        var localAccountId = account.LocalAccountId;

        await storeManager.SaveChangesAsync(cancellationToken);

        // The password an administrator sets replaces the one being guessed, so its failed attempts start over. The
        // change is committed, so clearing them runs on its own bounded token, a failure is logged, and the audit
        // record follows. See ADR-1237 and ADR-1351.
        await PostCommitSecurityEffects.ResetPasswordAttemptsAsync(
            tenantId,
            connection.ConnectionId,
            localAccountId
        );

        var resource = ToLocalAccountResource(account, creation.PrincipalId);

        var resourceValues = ToResourceValues(resource);
        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Created,
            resourceValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(account.ConcurrencyToken);
        return TypedResults.Created(
            $"/tenants/{tenantId}/connections/{connection.ConnectionId}/local-accounts/{localAccountId}",
            resource
        );
    }

    /// <summary>
    /// Lists the local accounts of a local connection, one page at a time.
    /// </summary>
    /// <response code="200">A page of local accounts.</response>
    /// <response code="404">The connection does not exist or is not a local connection.</response>
    internal virtual ValueTask<IResult> ListLocalAccountsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    ) => ListCoreAsync(httpContext, tenantId, connectionId, cursor, limit, cancellationToken);

    private async ValueTask<IResult> ListCoreAsync(
        HttpContext httpContext,
        string tenantId,
        string? connectionId,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        var effectiveLimit = NormalizeLimit(limit);

        var tenantResource = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantResource,
            Operations.List,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var connection = await FindConnectionAsync(
            httpContext,
            storeManager,
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        return await ProcessListAsync(
            httpContext,
            tenantResource,
            async () =>
            {
                var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
                var pageRequest = new PageRequest { Cursor = cursor, Limit = effectiveLimit };
                var page = await store.GetPageAsync(
                    connection.ConnectionId,
                    pageRequest,
                    cancellationToken
                );

                var localAccountIds = new List<string>(page.Items.Count);
                foreach (var account in page.Items)
                {
                    localAccountIds.Add(account.LocalAccountId);
                }

                var principalIds = await FindPrincipalIdsAsync(
                    storeManager,
                    connection.ConnectionId,
                    localAccountIds,
                    cancellationToken
                );

                var items = new List<(PersistedLocalAccount Account, string? PrincipalId)>(
                    page.Items.Count
                );
                foreach (var account in page.Items)
                {
                    var principalId = principalIds.GetValueOrDefault(account.LocalAccountId);
                    items.Add((account, principalId));
                }

                return new PagedResult<(PersistedLocalAccount Account, string? PrincipalId)>
                {
                    Items = items,
                    NextCursor = page.NextCursor,
                };
            },
            item => ToLocalAccountResource(item.Account, item.PrincipalId),
            cancellationToken
        );
    }

    /// <summary>
    /// Lists the local accounts of the platform, which are the accounts of the control-plane tenant's default
    /// connection, one page at a time.
    /// </summary>
    /// <response code="200">A page of local accounts.</response>
    /// <response code="404">The control-plane tenant's default connection does not exist or is not a local connection.</response>
    internal virtual async ValueTask<IResult> ListPlatformLocalAccountsAsync(
        HttpContext httpContext,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    )
    {
        var tenantId = httpContext.GetOpenIdContext().Tenant.TenantId;
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);
        return await ListCoreAsync(
            httpContext,
            tenantId,
            connectionId: null,
            cursor,
            limit,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a local account of the platform, in the control-plane tenant's default connection.
    /// </summary>
    /// <response code="201">The created local account.</response>
    /// <response code="400">The identifier the connection requires is missing or longer than allowed, the display name is longer than allowed, the claims include the reserved <c>name</c> claim, or the password does not meet the connection's password policy.</response>
    /// <response code="404">The control-plane tenant does not exist, or its default connection does not exist or is not a local connection.</response>
    /// <response code="409">A local account with the username or the email address already exists in the connection.</response>
    internal virtual async ValueTask<IResult> CreatePlatformLocalAccountAsync(
        HttpContext httpContext,
        [FromBody] CreateLocalAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        var tenantId = httpContext.GetOpenIdContext().Tenant.TenantId;
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);
        return await CreateCoreAsync(
            httpContext,
            tenantId,
            connectionId: null,
            request,
            cancellationToken
        );
    }

    /// <summary>
    /// Gets a local account of a local connection.
    /// </summary>
    /// <response code="200">The local account.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual async ValueTask<IResult> GetLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var tenantResource = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantResource,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var located = await FindAccountAsync(
                    storeManager,
                    connectionId,
                    localAccountId,
                    token
                );
                if (located is not { } found)
                {
                    return null;
                }

                var principalId = await FindPrincipalIdAsync(
                    storeManager,
                    found.Connection.ConnectionId,
                    found.Account.LocalAccountId,
                    token
                );
                return new LocalAccountWithPrincipal(found.Account, principalId);
            },
            value => ToLocalAccountResource(value.Account, value.PrincipalId),
            cancellationToken
        );
    }

    /// <summary>
    /// Updates the display name, the username and the email of a local account by applying a JSON Patch document.
    /// </summary>
    /// <response code="204">The local account was updated.</response>
    /// <response code="400">The JSON Patch document cannot be applied, the identifier the connection requires is missing or longer than allowed, or the display name is missing or longer than allowed.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    /// <response code="409">Another local account in the connection already has the username or the email address.</response>
    internal virtual async ValueTask<IResult> UpdateLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        [FromBody] JsonPatchDocument<UpdateLocalAccountRequest> request,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeUpdateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var (connection, store, account) = found;

        var model = new UpdateLocalAccountRequest
        {
            DisplayName = account.DisplayName,
            UserName = account.UserName,
            Email = account.Email,
            EmailVerified = account.EmailVerified,
        };

        try
        {
            request.ApplyTo(model);
        }
        catch (JsonPatchException)
        {
            return TypedResults.Problem(
                detail: "The JSON Patch document could not be applied.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        model.DisplayName = model.DisplayName?.Trim() ?? string.Empty;

        var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
        var expectedConcurrencyToken = string.IsNullOrEmpty(ifMatch) ? null : ifMatch;
        var error = await LocalAccountValidator.ValidateUpdateAsync(
            connection,
            account,
            model,
            expectedConcurrencyToken,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return ToErrorResult(error);
        }

        var principalId = await FindPrincipalIdAsync(
            storeManager,
            connection.ConnectionId,
            localAccountId,
            cancellationToken
        );

        account.DisplayName = model.DisplayName;
        account.UserName = model.UserName;
        account.Email = model.Email;
        account.EmailVerified = model.EmailVerified;
        await store.UpdateAsync(account, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var resource = ToLocalAccountResource(account, principalId);
        var resourceValues = ToResourceValues(resource);
        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Updated,
            resourceValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(account.ConcurrencyToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Deletes a local account and ends the sessions of its principal.
    /// </summary>
    /// <response code="204">The local account was deleted.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual async ValueTask<IResult> DeleteLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeDeleteAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var (connection, accountStore, account) = found;

        var error = await LocalAccountValidator.ValidateDeleteAsync(
            account,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return ToErrorResult(error);
        }

        // The tenant's context is created before anything is written, whether or not the account has an
        // identity: a tenant that is not served refuses the request here, so the answer depends on the
        // tenant, not on the account's shape.
        await using var tenantContext = await CreateTenantContextAsync(
            httpContext,
            tenantId,
            cancellationToken
        );

        // Cascade the account's identity in its connection, and the owning principal when this was its only identity.
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var identity = await identityStore.GetByConnectionSubjectAsync(
            connection.ConnectionId,
            localAccountId,
            cancellationToken
        );
        string? removedPrincipalId = null;
        if (identity is not null)
        {
            // The principal's sessions end before the deletion commits, while they can still be listed to notify their relying parties. See ADR-1229.
            await TerminateSessionsAsync(
                tenantContext,
                identity.PrincipalId,
                SessionTerminationReasons.AccountDeleted,
                cancellationToken
            );
        }

        await accountStore.RemoveAsync(localAccountId, cancellationToken);

        if (identity is not null)
        {
            await identityStore.RemoveAsync(identity.FederatedIdentityId, cancellationToken);

            var siblings = await identityStore.GetByPrincipalAsync(
                identity.PrincipalId,
                cancellationToken
            );
            if (
                siblings.All(sibling => sibling.FederatedIdentityId == identity.FederatedIdentityId)
            )
            {
                await storeManager
                    .GetStore<IFederatedPrincipalStore>()
                    .RemoveAsync(identity.PrincipalId, cancellationToken);
                removedPrincipalId = identity.PrincipalId;
            }
        }

        if (removedPrincipalId is null)
        {
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // The principal's role assignments are stored under the tenant of each node that holds them, so they are
            // removed, and the removal is saved, in an unscoped scope. See ADR-1018 and ADR-1212.
            using (AmbientTenantAccessor.BeginUnscoped(PrincipalRoleRemovalReason))
            {
                await storeManager
                    .GetStore<IRoleAssignmentStore>()
                    .RemoveByPrincipalAsync(removedPrincipalId, cancellationToken);
                await storeManager.SaveChangesAsync(cancellationToken);
            }
        }

        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Deleted,
            resourceValues: null
        );

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Enables a local account.
    /// </summary>
    /// <response code="204">The local account was enabled.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual ValueTask<IResult> EnableLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    ) =>
        SetEnabledAsync(
            httpContext,
            tenantId,
            connectionId,
            localAccountId,
            isEnabled: true,
            cancellationToken
        );

    /// <summary>
    /// Disables a local account and ends the sessions of its principal.
    /// </summary>
    /// <response code="204">The local account was disabled.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual ValueTask<IResult> DisableLocalAccountAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    ) =>
        SetEnabledAsync(
            httpContext,
            tenantId,
            connectionId,
            localAccountId,
            isEnabled: false,
            cancellationToken
        );

    private async ValueTask<IResult> SetEnabledAsync(
        HttpContext httpContext,
        string tenantId,
        string connectionId,
        string localAccountId,
        bool isEnabled,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeUpdateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var (connection, store, account) = found;

        // The account's principal is resolved before the account is written, for the audit record and, when the account
        // is disabled, to end its sessions. Disabling an account with an identity ends its principal's
        // sessions, so the tenant's context is created before the account is written: a tenant that is not served
        // refuses the request here, before anything is committed or audited. Enabling an account, or disabling one
        // without an identity, ends no session and needs no context.
        var principalId = await FindPrincipalIdAsync(
            storeManager,
            connection.ConnectionId,
            localAccountId,
            cancellationToken
        );

        await using var tenantContext =
            isEnabled || principalId is null
                ? null
                : await CreateTenantContextAsync(httpContext, tenantId, cancellationToken);

        account.IsEnabled = isEnabled;
        await store.UpdateAsync(account, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        if (tenantContext is not null && principalId is not null)
        {
            // A disabled account's sessions end, which revokes every grant of its principal and notifies its relying
            // parties. The change is committed, so the termination runs on its own bounded token, a failure is
            // logged, and the audit record follows. See ADR-1229 and ADR-1351.
            await PostCommitSecurityEffects.TerminateSessionsAsync(
                tenantContext,
                principalId,
                SessionTerminationReasons.AccountDisabled
            );
        }

        var resource = ToLocalAccountResource(account, principalId);
        var resourceValues = ToResourceValues(resource);
        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Updated,
            resourceValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(account.ConcurrencyToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Sets the password of a local account and ends the sessions of its principal.
    /// </summary>
    /// <response code="204">The password was set.</response>
    /// <response code="400">The password does not meet the connection's password policy.</response>
    /// <response code="404">The connection, the local account or the tenant does not exist, or the connection is not a local connection.</response>
    internal virtual async ValueTask<IResult> ResetPasswordAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        [FromBody] ResetLocalAccountPasswordRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeUpdateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var (connection, store, account) = found;

        var passwordError = await LocalAccountPasswords.ValidateAsync(
            storeManager,
            connection,
            account,
            request.Password,
            cancellationToken
        );
        if (passwordError is not null)
        {
            return ToErrorResult(passwordError);
        }

        // An account with an identity has its principal's sessions ended, so the tenant's context is created
        // before the password is written: a tenant that is not served refuses the request here, before anything is
        // committed or audited. An account without one ends no session and needs no context.
        var principalId = await FindPrincipalIdAsync(
            storeManager,
            connection.ConnectionId,
            localAccountId,
            cancellationToken
        );

        await using var tenantContext = principalId is null
            ? null
            : await CreateTenantContextAsync(httpContext, tenantId, cancellationToken);

        await LocalAccountPasswords.WithPasswordBytesAsync(
            request.Password,
            (bytes, ct) => store.SetPasswordAsync(localAccountId, bytes, ct),
            cancellationToken
        );
        await storeManager.SaveChangesAsync(cancellationToken);

        if (tenantContext is not null && principalId is not null)
        {
            // A reset password ends the sessions of the account's principal and revokes its grants, so the tokens of a
            // compromised account stop at once. The change is committed, so the termination runs on its own bounded
            // token, a failure is logged, and the audit record follows. See ADR-1229 and ADR-1351.
            await PostCommitSecurityEffects.TerminateSessionsAsync(
                tenantContext,
                principalId,
                SessionTerminationReasons.PasswordReset
            );
        }

        // The password an administrator sets replaces the one being guessed, so its failed attempts start over. The
        // change is committed, so clearing them runs on its own bounded token, a failure is logged, and the audit
        // record follows. See ADR-1237 and ADR-1351.
        await PostCommitSecurityEffects.ResetPasswordAttemptsAsync(
            tenantId,
            connection.ConnectionId,
            account.LocalAccountId
        );

        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Updated,
            resourceValues: null
        );

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Gets the claims of a local account.
    /// </summary>
    /// <response code="200">The claims of the local account.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual async ValueTask<IResult> GetClaimsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var tenantResource = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantResource,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var located = await FindAccountAsync(
                    storeManager,
                    connectionId,
                    localAccountId,
                    token
                );
                return located?.Account;
            },
            value => ToClaimsResource(tenantId, value),
            cancellationToken
        );
    }

    /// <summary>
    /// Replaces the claims of a local account.
    /// </summary>
    /// <response code="204">The claims were replaced.</response>
    /// <response code="400">The claims include the reserved <c>name</c> claim, which is the account's display name.</response>
    /// <response code="404">The connection or the local account does not exist, or the connection is not a local connection.</response>
    internal virtual async ValueTask<IResult> SetClaimsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        [FromBody] SetLocalAccountClaimsRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeUpdateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var store = found.Store;

        var claimTypes = request.Claims.Select(claim => claim.Type);
        var reservedClaimError = LocalAccountClaimRules.GetReservedClaimErrorOrDefault(claimTypes);
        if (reservedClaimError is not null)
        {
            return ToErrorResult(reservedClaimError);
        }

        var claims = request
            .Claims.Select(claim => new PersistedLocalAccountClaim
            {
                Type = claim.Type,
                Value = claim.Value,
            })
            .ToList();
        await store.ReplaceClaimsAsync(localAccountId, claims, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Updated,
            resourceValues: null
        );

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Gets the profile and system metadata of the principal of a local account.
    /// </summary>
    /// <response code="200">The metadata of the principal.</response>
    /// <response code="404">The connection or the local account does not exist, the connection is not a local connection, or the account has no principal.</response>
    internal virtual async ValueTask<IResult> GetMetadataAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var tenantResource = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantResource,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                return await ResolvePrincipalAsync(
                    storeManager,
                    connectionId,
                    localAccountId,
                    token
                );
            },
            value => ToMetadataResource(tenantId, localAccountId, value),
            cancellationToken
        );
    }

    /// <summary>
    /// Sets the profile and system metadata that the request supplies on the principal of a local account.
    /// </summary>
    /// <response code="204">The metadata was set.</response>
    /// <response code="404">The connection or the local account does not exist, the connection is not a local connection, or the account has no principal.</response>
    internal virtual async ValueTask<IResult> SetMetadataAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromRoute] string localAccountId,
        [FromBody] SetLocalAccountMetadataRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationError = await LocalAccountValidator.AuthorizeUpdateAsync(
            httpContext.User,
            tenantId,
            cancellationToken
        );
        if (authorizationError is not null)
        {
            return ToErrorResult(authorizationError);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var principalStore = storeManager.GetStore<IFederatedPrincipalStore>();

        var principal = await ResolvePrincipalAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (principal is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        // Set-when-present: a missing bag leaves the current value untouched.
        if (request.ProfileMetadata is { } profileMetadata)
        {
            principal.ProfileMetadata = profileMetadata;
        }

        if (request.SystemMetadata is { } systemMetadata)
        {
            principal.SystemMetadata = systemMetadata;
        }

        await principalStore.UpdateAsync(principal, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await ManagementAuditRecorder.RecordLocalAccountChangedAsync(
            httpContext,
            tenantId,
            localAccountId,
            ResourceChangeTypes.Updated,
            resourceValues: null
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(principal.ConcurrencyToken);
        return TypedResults.NoContent();
    }

    private static LocalAccountClaimsResource ToClaimsResource(
        string tenantId,
        PersistedLocalAccount account
    ) =>
        new()
        {
            TenantId = tenantId,
            LocalAccountId = account.LocalAccountId,
            Claims = account
                .Claims.Select(claim => new LocalAccountClaim
                {
                    Type = claim.Type,
                    Value = claim.Value,
                })
                .ToList(),
            ConcurrencyToken = account.ConcurrencyToken,
        };

    private static LocalAccountMetadataResource ToMetadataResource(
        string tenantId,
        string localAccountId,
        PersistedFederatedPrincipal principal
    ) =>
        new()
        {
            TenantId = tenantId,
            LocalAccountId = localAccountId,
            PrincipalId = principal.PrincipalId,
            ProfileMetadata = principal.ProfileMetadata,
            SystemMetadata = principal.SystemMetadata,
            ConcurrencyToken = principal.ConcurrencyToken,
        };

    private async ValueTask<PersistedFederatedPrincipal?> ResolvePrincipalAsync(
        IStoreManager storeManager,
        string connectionId,
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var located = await FindAccountAsync(
            storeManager,
            connectionId,
            localAccountId,
            cancellationToken
        );
        if (located is not { } found)
        {
            return null;
        }

        var identity = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByConnectionSubjectAsync(
                found.Connection.ConnectionId,
                localAccountId,
                cancellationToken
            );
        if (identity is null)
        {
            return null;
        }

        return await storeManager
            .GetStore<IFederatedPrincipalStore>()
            .GetOrDefaultAsync(identity.PrincipalId, cancellationToken);
    }

    // Loads the connection that a route names, or the control-plane tenant's default connection when the route names
    // none, as the platform routes do. A connection that is missing, or whose strategy is not local, is absent.
    private static async ValueTask<PersistedConnection?> FindConnectionAsync(
        HttpContext httpContext,
        IStoreManager storeManager,
        string? connectionId,
        CancellationToken cancellationToken
    )
    {
        var connectionStore = storeManager.GetStore<IConnectionStore>();

        PersistedConnection? connection;
        if (connectionId is null)
        {
            var settings = httpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
            var defaultName = settings.GetValue(OpenIdSettingKeys.DefaultConnection);
            connection = await connectionStore.GetByNameOrDefaultAsync(
                defaultName,
                cancellationToken
            );
        }
        else
        {
            return await FindLocalConnectionByIdAsync(
                storeManager,
                connectionId,
                cancellationToken
            );
        }

        return connection is not null && ConnectionStrategyNames.IsLocal(connection.Strategy)
            ? connection
            : null;
    }

    // Loads a connection by identifier. A connection that is missing, or whose strategy is not local, is absent.
    private static async ValueTask<PersistedConnection?> FindLocalConnectionByIdAsync(
        IStoreManager storeManager,
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var connection = await storeManager
            .GetStore<IConnectionStore>()
            .GetOrDefaultAsync(connectionId, cancellationToken);
        return connection is not null && ConnectionStrategyNames.IsLocal(connection.Strategy)
            ? connection
            : null;
    }

    // Loads a local account of the connection that a route names, with the connection and the store that holds it. An
    // account that another connection holds is absent.
    private async ValueTask<(
        PersistedConnection Connection,
        ILocalAccountStore Store,
        PersistedLocalAccount Account
    )?> FindAccountAsync(
        IStoreManager storeManager,
        string connectionId,
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var connection = await FindLocalConnectionByIdAsync(
            storeManager,
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return null;
        }

        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        var account = await store.GetByIdOrDefaultAsync(localAccountId, cancellationToken);
        if (
            account is null
            || !string.Equals(
                account.ConnectionId,
                connection.ConnectionId,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        return (connection, store, account);
    }

    // Materializes a context for the addressed tenant so session termination runs the seam (grant revocation,
    // back-channel logout, auditing) in that tenant's security domain rather than the request's resolved tenant. A
    // tenant that is not served refuses here, so a route creates the context before its first write.
    private ValueTask<OpenIdContext> CreateTenantContextAsync(
        HttpContext httpContext,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var mediator = httpContext.RequestServices.GetRequiredService<IMediator>();
        return OpenIdContextFactory.CreateForTenantAsync(
            httpContext,
            tenantId,
            mediator,
            cancellationToken
        );
    }

    private async ValueTask TerminateSessionsAsync(
        OpenIdContext tenantContext,
        string principalId,
        string reason,
        CancellationToken cancellationToken
    ) => await SessionService.RevokeAllAsync(tenantContext, principalId, reason, cancellationToken);

    // Resolves the principal behind the account's identity in its connection, if the account has one. A route resolves
    // it before its write, so the effect that follows the commit is one call.
    private static async ValueTask<string?> FindPrincipalIdAsync(
        IStoreManager storeManager,
        string connectionId,
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var identity = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByConnectionSubjectAsync(connectionId, localAccountId, cancellationToken);
        return identity?.PrincipalId;
    }

    // Resolves the principals behind the identities of a page of accounts in one store call, keyed by the local
    // account id. An account with no identity has no entry.
    private static async ValueTask<IReadOnlyDictionary<string, string>> FindPrincipalIdsAsync(
        IStoreManager storeManager,
        string connectionId,
        IReadOnlyCollection<string> localAccountIds,
        CancellationToken cancellationToken
    )
    {
        var identities = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByConnectionSubjectsAsync(connectionId, localAccountIds, cancellationToken);
        var principalIds = new Dictionary<string, string>(
            identities.Count,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var identity in identities)
        {
            principalIds[identity.Subject] = identity.PrincipalId;
        }

        return principalIds;
    }
}
