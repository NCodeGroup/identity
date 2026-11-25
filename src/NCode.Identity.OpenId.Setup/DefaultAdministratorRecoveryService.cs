// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Accounts.Management;
using NCode.Identity.OpenId.Accounts.Management.Auditing;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Setup.Logging;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Provides a default implementation of <see cref="IAdministratorRecoveryService"/>. The administrator is a local
/// account in the <c>local</c> connection of the anchor tenant. In the control-plane cell it holds
/// <see cref="BuiltInRoles.GlobalAdmin"/> at the platform node; in a workload cell with a management tenant it holds
/// <see cref="BuiltInRoles.CellAdmin"/> at the cell's node, and any other cell refuses recovery. An existing account is
/// enabled and gets the new password, a missing one is created as setup creates the root administrator, and the role
/// assignment is added when the principal lacks it. The changes are staged on one store manager and saved once, then
/// the account's failed password attempts are cleared and each change is audited.
/// </summary>
// See ADR-1536.
internal sealed class DefaultAdministratorRecoveryService(
    IStoreManagerFactory storeManagerFactory,
    ITenantResolver tenantResolver,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ILocalAccountCreator localAccountCreator,
    LocalAccountStoreCatalog localAccountStoreCatalog,
    IPasswordAttemptLimiter passwordAttemptLimiter,
    IManagementAuditRecorder managementAuditRecorder,
    ICryptoService cryptoService,
    IPlatformTopology platformTopology,
    IOptions<CellOptions> cellOptions,
    ILogger<DefaultAdministratorRecoveryService> logger
) : IAdministratorRecoveryService
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private ITenantResolver TenantResolver { get; } = tenantResolver;

    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private ILocalAccountCreator LocalAccountCreator { get; } = localAccountCreator;

    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    private IPasswordAttemptLimiter PasswordAttemptLimiter { get; } = passwordAttemptLimiter;

    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;

    private ICryptoService CryptoService { get; } = cryptoService;

    private IPlatformTopology PlatformTopology { get; } = platformTopology;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private ILogger<DefaultAdministratorRecoveryService> Logger { get; } = logger;

    private static AdministratorRecoveryResult CreateError(int statusCode, string detail) =>
        new()
        {
            Error = new ManagementError { StatusCode = statusCode, Detail = detail },
        };

    /// <inheritdoc />
    public async ValueTask<AdministratorRecoveryResult> RecoverAsync(
        RecoveryAdminOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!PlatformTopology.IsControlPlaneCell && !PlatformTopology.HasManagementTenant)
        {
            return CreateError(
                StatusCodes.Status404NotFound,
                "Recovery runs in the control-plane cell or in a cell with a management tenant."
            );
        }

        var userName = options.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return CreateError(
                StatusCodes.Status400BadRequest,
                "The administrator's username is required."
            );
        }

        if (
            options.Password is not { } password
            || password.Length < SetupDefaults.MinimumRootPasswordLength
        )
        {
            return CreateError(
                StatusCodes.Status400BadRequest,
                $"The administrator's password must be at least {SetupDefaults.MinimumRootPasswordLength} characters."
            );
        }

        // The control-plane tenant is resolved in a scope of its own, as setup resolves it, and the scope stays open
        // for the audit records, which resolve the tenant's environment from its services.
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var tenant = await TenantResolver.ResolveControlPlaneTenantAsync(
            httpContext,
            cancellationToken
        );
        var tenantId = tenant.TenantId;

        // The role assignments of a principal are read across tenants, so the work runs unscoped, and the tenant's rows
        // are staged in its own scope inside it. See ADR-1018.
        using var unscopedScope = AmbientTenantAccessor.BeginUnscoped("administrator recovery");

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        RecoveredAdministrator recovered;
        using (AmbientTenantAccessor.BeginScope(tenantId))
        {
            var connection = await storeManager
                .GetStore<IConnectionStore>()
                .GetByNameOrDefaultAsync(ConnectionDefaults.Name, cancellationToken);
            if (connection is null)
            {
                return CreateError(
                    StatusCodes.Status500InternalServerError,
                    $"The control-plane tenant has no '{ConnectionDefaults.Name}' connection."
                );
            }

            var staged = await StageAccountAsync(
                storeManager,
                connection,
                options,
                userName,
                password,
                cancellationToken
            );
            if (staged.Error is not null)
                return new AdministratorRecoveryResult { Error = staged.Error };

            recovered = staged;

            var assignmentId = await StageAdministratorAsync(
                storeManager,
                tenantId,
                recovered.PrincipalId,
                cancellationToken
            );
            recovered = recovered with { AssignmentId = assignmentId };

            await storeManager.SaveChangesAsync(cancellationToken);

            var attemptKey = LocalAccountAttemptKeys.ForAccount(recovered.LocalAccountId);
            await PasswordAttemptLimiter.ResetAsync(
                tenantId,
                connection.ConnectionId,
                attemptKey,
                cancellationToken
            );
        }

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var tenantCommand = new TenantChangedCommand(tenantId, SettingsChanged: false);
        await mediator.SendAsync(tenantCommand, cancellationToken);

        await RecordAuditAsync(httpContext, tenantId, recovered);

        Logger.AdministratorRecovered(tenantId, recovered.PrincipalId, recovered.Created);

        return new AdministratorRecoveryResult
        {
            TenantId = tenantId,
            PrincipalId = recovered.PrincipalId,
            Created = recovered.Created,
        };
    }

    private async ValueTask<RecoveredAdministrator> StageAccountAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        RecoveryAdminOptions options,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        var account = await store.GetByUserNameOrDefaultAsync(
            connection.ConnectionId,
            userName,
            cancellationToken
        );

        if (account is null)
        {
            var createRequest = new CreateLocalAccountRequest
            {
                UserName = userName,
                DisplayName = options.DisplayName,
                Password = password,
                Email = options.Email,
                EmailVerified = false,
                IsEnabled = true,
            };
            var creation = await LocalAccountCreator.StageAsync(
                storeManager,
                connection,
                createRequest,
                cancellationToken
            );
            if (!creation.Succeeded)
                return new RecoveredAdministrator { Error = creation.Error };

            return new RecoveredAdministrator
            {
                PrincipalId = creation.PrincipalId,
                LocalAccountId = creation.Account.LocalAccountId,
                Created = true,
            };
        }

        var identity = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByConnectionSubjectAsync(
                connection.ConnectionId,
                account.LocalAccountId,
                cancellationToken
            );
        if (identity is null)
        {
            return new RecoveredAdministrator
            {
                Error = new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = $"The account '{userName}' has no principal to restore.",
                },
            };
        }

        if (!account.IsEnabled)
        {
            account.IsEnabled = true;
            await store.UpdateAsync(account, cancellationToken);
        }

        // The buffer is pinned and zeroed on dispose, so the plaintext never lingers on the GC heap.
        using var owner = SecureEncoding.UTF8.RentBytes(
            password,
            isSensitive: true,
            out Memory<byte> passwordBytes
        );
        await store.SetPasswordAsync(account.LocalAccountId, passwordBytes, cancellationToken);

        return new RecoveredAdministrator
        {
            PrincipalId = identity.PrincipalId,
            LocalAccountId = account.LocalAccountId,
            Created = false,
        };
    }

    private (string RoleName, string ResourceType, string ResourceId) GetAdministratorNode() =>
        PlatformTopology.HasManagementTenant
            ? (BuiltInRoles.CellAdmin, OpenIdResourceTypes.Cell, CellOptions.Name)
            : (
                BuiltInRoles.GlobalAdmin,
                OpenIdResourceTypes.Platform,
                OpenIdConstants.PlatformResourceId
            );

    // The role that confers every management authority (ADR-1417) is held at the platform node, and in a cell with a
    // management tenant the cell's administrator holds its role at the cell's node. The assignment is staged only when
    // the principal does not hold it, and its identifier is returned then.
    private async ValueTask<string?> StageAdministratorAsync(
        IStoreManager storeManager,
        string tenantId,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var (roleName, resourceType, resourceId) = GetAdministratorNode();
        var store = storeManager.GetStore<IRoleAssignmentStore>();
        var existing = await store.GetByResourceAsync(resourceType, resourceId, cancellationToken);
        var isHeld = existing.Any(item =>
            item.PrincipalId == principalId && item.RoleName == roleName
        );
        if (isHeld)
            return null;

        var assignmentId = CryptoService.GenerateResourceId();
        var assignment = new PersistedRoleAssignment
        {
            TenantId = tenantId,
            AssignmentId = assignmentId,
            PrincipalId = principalId,
            RoleName = roleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ConcurrencyToken = string.Empty,
        };
        await store.AddAsync(assignment, cancellationToken);
        return assignmentId;
    }

    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The record follows a committed change and runs on a bounded token of the recorder's own, so no caller can pass a token that drops it (ADR-1351)."
    )]
    private async ValueTask RecordAuditAsync(
        HttpContext httpContext,
        string tenantId,
        RecoveredAdministrator recovered
    )
    {
        var changeType = recovered.Created
            ? ResourceChangeTypes.Created
            : ResourceChangeTypes.Updated;
        var accountEvent = new LocalAccountChangedAuditEvent
        {
            EventSource = AuditEventSources.Management,
            TenantId = tenantId,
            SubjectId = recovered.PrincipalId,
            ResourceId = recovered.LocalAccountId,
            ChangeType = changeType,
            ResourceValues = null,
            Outcome = AuditOutcome.Success,
        };
        await ManagementAuditRecorder.RecordResourceChangeAsync(httpContext, accountEvent);

        if (recovered.AssignmentId is not { } assignmentId)
            return;

        var (_, nodeType, nodeId) = GetAdministratorNode();
        var assignmentEvent = new RoleAssignmentChangedAuditEvent
        {
            EventSource = AuditEventSources.Management,
            TenantId = tenantId,
            SubjectId = recovered.PrincipalId,
            ResourceId = assignmentId,
            NodeType = nodeType,
            NodeId = nodeId,
            ChangeType = ResourceChangeTypes.Created,
            ResourceValues = null,
            Outcome = AuditOutcome.Success,
        };
        await ManagementAuditRecorder.RecordResourceChangeAsync(httpContext, assignmentEvent);
    }

    private sealed record RecoveredAdministrator
    {
        public string PrincipalId { get; init; } = string.Empty;

        public string LocalAccountId { get; init; } = string.Empty;

        public bool Created { get; init; }

        public string? AssignmentId { get; init; }

        public ManagementError? Error { get; init; }
    }
}
