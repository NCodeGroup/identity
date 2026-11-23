// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Accounts.Management;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Setup.Contracts;
using NCode.Identity.OpenId.Setup.Logging;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Provides a default implementation of <see cref="IInstanceSetupService"/>. The root administrator is a local account
/// in the control-plane tenant that holds <see cref="BuiltInRoles.GlobalAdmin"/> at the platform node. The account, its
/// identity, the role assignment, the management console's client and the automation client when the request carries
/// them, and the initialized mark are staged on one store manager and saved once, so the instance is initialized
/// exactly when its root administrator exists. The automation client is a platform-owned machine client that holds
/// <see cref="BuiltInRoles.GlobalAdmin"/> at the platform node. Both clients are staged through
/// <see cref="IClientCreator"/>, so each passes the same validation as a client that the management API creates. Setup
/// updates the singleton row that a host replica may have written for its generated token, and runs again, up to
/// <see cref="MaxSetUpAttempts"/> times, when a replica changes that row while it runs.
/// </summary>
internal sealed class DefaultInstanceSetupService(
    IStoreManagerFactory storeManagerFactory,
    ITenantResolver tenantResolver,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ILocalAccountCreator localAccountCreator,
    IClientCreator clientCreator,
    ICryptoService cryptoService,
    IOptions<SetupInstanceLinksOptions> instanceLinksOptions,
    TimeProvider timeProvider,
    ILogger<DefaultInstanceSetupService> logger
) : IInstanceSetupService
{
    private const string BffPathPrefix = "/bff";

    /// <summary>
    /// The number of attempts that <see cref="SetUpAsync"/> runs before it gives up because host replicas keep changing
    /// the singleton setup row.
    /// </summary>
    internal const int MaxSetUpAttempts = 5;

    internal const string BffCallbackPath = BffPathPrefix + "/callback";

    internal const string BffBackChannelLogoutPath = BffPathPrefix + "/backchannel-logout";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private ITenantResolver TenantResolver { get; } = tenantResolver;

    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private ILocalAccountCreator LocalAccountCreator { get; } = localAccountCreator;

    private IClientCreator ClientCreator { get; } = clientCreator;

    private ICryptoService CryptoService { get; } = cryptoService;

    private SetupInstanceLinksOptions InstanceLinksOptions { get; } = instanceLinksOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ILogger<DefaultInstanceSetupService> Logger { get; } = logger;

    private static ManagementError CreateError(int statusCode, string detail) =>
        new() { StatusCode = statusCode, Detail = detail };

    private static InstanceSetupResult CreateAlreadySetUpResult() =>
        new()
        {
            Error = CreateError(StatusCodes.Status404NotFound, "The instance is already set up."),
        };

    /// <inheritdoc />
    public async ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IInstanceSetupStore>();

        var setup = await store.GetOrDefaultAsync(cancellationToken);
        return setup?.IsInitialized ?? false;
    }

    // The console registers as a confidential client that authenticates with a signed assertion, pushes its
    // authorization requests and holds sender-constrained tokens (ADR-1437 decision 6).
    private static JsonElement CreateManagementConsoleSettings(string baseUrl)
    {
        var origin = baseUrl.TrimEnd('/');

        var settings = new Dictionary<string, object>
        {
            [OpenIdSettingNames.TokenEndpointAuthMethod] = OpenIdProtocol
                .ClientAuthenticationMethods
                .PrivateKeyJwt,
            [OpenIdSettingNames.RequirePushedAuthorizationRequests] = true,
            [OpenIdSettingNames.RequireDPoPBoundAccessTokens] = true,
            [OpenIdSettingNames.GrantTypesSupported] = new[]
            {
                OpenIdProtocol.GrantTypes.AuthorizationCode,
                OpenIdProtocol.GrantTypes.RefreshToken,
            },
            [OpenIdSettingNames.ResponseTypesSupported] = new[]
            {
                OpenIdProtocol.ResponseTypes.Code,
            },
            [OpenIdSettingNames.RedirectUris] = new[] { $"{origin}{BffCallbackPath}" },
            [OpenIdSettingNames.PostLogoutRedirectUris] = new[] { $"{origin}/" },
            [OpenIdSettingNames.BackChannelLogoutUri] = $"{origin}{BffBackChannelLogoutPath}",
            [OpenIdSettingNames.BackChannelLogoutSessionRequired] = true,
        };
        return JsonSerializer.SerializeToElement(settings);
    }

    private static JsonElement CreateAutomationSettings()
    {
        var settings = new Dictionary<string, object>
        {
            [OpenIdSettingNames.GrantTypesSupported] = new[]
            {
                OpenIdProtocol.GrantTypes.ClientCredentials,
            },
            [OpenIdSettingNames.TokenEndpointAuthMethod] = OpenIdProtocol
                .ClientAuthenticationMethods
                .PrivateKeyJwt,
        };
        return JsonSerializer.SerializeToElement(settings);
    }

    // The addresses come from the control-plane tenant's issuer, never from the request, so a request's Host header
    // cannot choose where the done page sends the operator. The management API and the API reference sit at the host
    // root, outside any tenant path, so they hang off the issuer's origin.
    private static string GetOrigin(string issuer) =>
        new Uri(issuer).GetLeftPart(UriPartial.Authority);

    private static string GetDiscoveryUrl(string issuer) =>
        issuer.TrimEnd('/') + OpenIdConstants.EndpointPaths.Discovery;

    private static string GetManagementApiUrl(string issuer) =>
        GetOrigin(issuer) + ManagementApiDefaults.RoutePrefix;

    private static string? GetApiReferenceUrl(string issuer, string? apiReferencePath) =>
        apiReferencePath is null ? null : GetOrigin(issuer) + apiReferencePath;

    private static InstanceSetupResult CreateClientFailureResult(
        ClientCreation creation,
        string client
    )
    {
        if (creation.Error is { } error)
            return new InstanceSetupResult { Error = error };

        var detailMessages =
            creation.SettingErrors?.SelectMany(pair =>
                pair.Value.Select(message => $"{pair.Key}: {message}")
            )
            ?? [];
        var detail = string.Join(" ", detailMessages);

        return new InstanceSetupResult
        {
            Error = CreateError(
                StatusCodes.Status400BadRequest,
                $"The {client} cannot be registered. {detail}"
            ),
        };
    }

    // The role that confers every management authority (ADR-1417) is held at the platform node.
    private async ValueTask StageGlobalAdminAsync(
        IStoreManager storeManager,
        string tenantId,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<IRoleAssignmentStore>();
        var assignment = new PersistedRoleAssignment
        {
            TenantId = tenantId,
            AssignmentId = CryptoService.GenerateResourceId(),
            PrincipalId = principalId,
            RoleName = BuiltInRoles.GlobalAdmin,
            ResourceType = OpenIdResourceTypes.Platform,
            ResourceId = OpenIdConstants.PlatformResourceId,
            ConcurrencyToken = string.Empty,
        };
        await store.AddAsync(assignment, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<InstanceSetupResult> SetUpAsync(
        InstanceSetupRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = request.Root;

        if (string.IsNullOrWhiteSpace(root.UserName))
        {
            return new InstanceSetupResult
            {
                Error = CreateError(
                    StatusCodes.Status400BadRequest,
                    "The root administrator's username is required."
                ),
            };
        }

        if (
            root.Password is not { } password
            || password.Length < SetupDefaults.MinimumRootPasswordLength
        )
        {
            return new InstanceSetupResult
            {
                Error = CreateError(
                    StatusCodes.Status400BadRequest,
                    $"The root administrator's password must be at least {SetupDefaults.MinimumRootPasswordLength} characters."
                ),
            };
        }

        (string ClientId, string BaseUrl, string Jwks)? consoleRegistration = null;
        if (request.ManagementConsole is { } console)
        {
            if (console.BaseUrl is not { } baseUrl || console.Jwks is not { } jwks)
            {
                return new InstanceSetupResult
                {
                    Error = CreateError(
                        StatusCodes.Status400BadRequest,
                        "The management console needs a base URL and a JWK Set."
                    ),
                };
            }

            consoleRegistration = (console.ClientId, baseUrl, jwks);
        }

        (string ClientId, string Jwks)? automationRegistration = null;
        if (request.Automation is { } automation)
        {
            if (
                automation.ClientId is not { } requestedClientId
                || automation.Jwks is not { } requestedJwks
            )
            {
                return new InstanceSetupResult
                {
                    Error = CreateError(
                        StatusCodes.Status400BadRequest,
                        "The automation client needs a client ID and a JWK Set."
                    ),
                };
            }

            automationRegistration = (requestedClientId, requestedJwks);
        }

        // Resolving the tenant creates the control-plane tenant on demand, so the principal and the role assignment
        // staged below have a tenant to reference even on a database that no replica has served. The resolver needs an
        // HTTP context only for its request-scoped services, so one is built over a scope of its own, as for other
        // work outside a request.
        string tenantId;
        string issuer;
        await using (var scope = ServiceScopeFactory.CreateAsyncScope())
        {
            var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var tenant = await TenantResolver.ResolveControlPlaneTenantAsync(
                httpContext,
                cancellationToken
            );
            tenantId = tenant.TenantId;

            // A served tenant always has an issuer, and the control-plane tenant is served.
            issuer =
                tenant.Issuer
                ?? throw new InvalidOperationException(
                    "The control-plane tenant has no issuer to compute the instance's addresses from."
                );
        }

        // The platform's singleton setup row is read and written unscoped. See ADR-1018.
        using var unscopedScope = AmbientTenantAccessor.BeginUnscoped("instance setup");

        var staging = new InstanceSetupStaging
        {
            TenantId = tenantId,
            Issuer = issuer,
            UserName = root.UserName,
            Password = password,
            Root = root,
            ManagementConsole = consoleRegistration,
            Automation = automationRegistration,
        };

        for (var attempt = 0; attempt < MaxSetUpAttempts; attempt++)
        {
            var result = await TrySetUpAsync(staging, cancellationToken);
            if (result is not null)
                return result;
        }

        throw new InvalidOperationException(
            $"The instance setup row changed during each of the {MaxSetUpAttempts} setup attempts."
        );
    }

    /// <summary>
    /// Runs one attempt to set up the instance on a store manager of its own. The attempt returns
    /// <see langword="null"/> when a host replica stored its setup token on the singleton row while the attempt ran, so
    /// the caller runs another attempt.
    /// </summary>
    /// <param name="staging">The <see cref="InstanceSetupStaging"/> that carries the validated inputs of the
    /// setup.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="InstanceSetupResult"/> of the attempt, or <see langword="null"/> when the attempt lost the singleton
    /// row to a host replica.</returns>
    private async ValueTask<InstanceSetupResult?> TrySetUpAsync(
        InstanceSetupStaging staging,
        CancellationToken cancellationToken
    )
    {
        var tenantId = staging.TenantId;
        var issuer = staging.Issuer;
        var root = staging.Root;
        var password = staging.Password;
        var consoleRegistration = staging.ManagementConsole;
        var automationRegistration = staging.Automation;

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var setupStore = storeManager.GetStore<IInstanceSetupStore>();

        // A host replica that generates its setup token writes the same row, so setup adds the row in a unit of work of
        // its own, where the first writer wins, and always updates it.
        var setup =
            await setupStore.GetOrDefaultAsync(cancellationToken)
            ?? await InstanceSetupRecords.GetOrAddAsync(StoreManagerFactory, cancellationToken);
        if (setup.IsInitialized)
            return CreateAlreadySetUpResult();

        string principalId;
        string displayName;
        string? consoleClientId = null;
        string? automationClientId = null;

        // The control-plane tenant's rows are staged in its own tenant scope, inside the unscoped one, so a
        // tenant-bound store finds and names them in that tenant alone.
        using (AmbientTenantAccessor.BeginScope(tenantId))
        {
            var createRequest = new CreateLocalAccountRequest
            {
                UserName = staging.UserName,
                DisplayName = root.DisplayName,
                Password = password,
                Email = root.Email,
                EmailVerified = false,
                IsEnabled = true,
            };
            var creation = await LocalAccountCreator.StageAsync(
                storeManager,
                tenantId,
                createRequest,
                cancellationToken
            );
            if (!creation.Succeeded)
                return new InstanceSetupResult { Error = creation.Error };

            principalId = creation.PrincipalId;
            displayName = creation.Account.DisplayName;

            await StageGlobalAdminAsync(storeManager, tenantId, principalId, cancellationToken);

            if (consoleRegistration is { } registration)
            {
                var consoleRequest = new ClientCreationRequest
                {
                    TenantId = tenantId,
                    ClientId = registration.ClientId,
                    IsDisabled = false,
                    Settings = CreateManagementConsoleSettings(registration.BaseUrl),
                    Jwks = JsonSerializer.Deserialize<JsonElement>(registration.Jwks),
                };
                var clientCreation = await ClientCreator.StageAsync(
                    storeManager,
                    consoleRequest,
                    cancellationToken
                );
                if (!clientCreation.Succeeded)
                    return CreateClientFailureResult(clientCreation, "management console's client");

                consoleClientId = clientCreation.Client.ClientId;
            }

            if (automationRegistration is { } automationClient)
            {
                // A machine client lives in the control-plane tenant and is owned by the platform node, as the
                // management API creates it, so the creator requires it to be limited to the client_credentials grant.
                var machineRequest = new ClientCreationRequest
                {
                    TenantId = tenantId,
                    ClientId = automationClient.ClientId,
                    IsDisabled = false,
                    OwningNodeType = OpenIdResourceTypes.Platform,
                    OwningNodeId = OpenIdConstants.PlatformResourceId,
                    Settings = CreateAutomationSettings(),
                    Jwks = JsonSerializer.Deserialize<JsonElement>(automationClient.Jwks),
                };
                var machineCreation = await ClientCreator.StageAsync(
                    storeManager,
                    machineRequest,
                    cancellationToken
                );
                if (!machineCreation.Succeeded)
                    return CreateClientFailureResult(machineCreation, "automation client");

                await StageGlobalAdminAsync(
                    storeManager,
                    tenantId,
                    machineCreation.Client.PrincipalId,
                    cancellationToken
                );

                automationClientId = machineCreation.Client.ClientId;
            }
        }

        // The update carries the concurrency token of the row read or added above.
        setup.IsInitialized = true;
        setup.InitializedAt = TimeProvider.GetUtcNow();
        setup.InitializedByPrincipalId = principalId;
        setup.TokenHash = null;
        setup.TokenIssuedAt = null;
        setup.TokenExpiresAt = null;

        try
        {
            await setupStore.UpdateAsync(setup, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        catch (StoreConcurrencyException)
        {
            // The row changed between the read above and this write. Another setup marked the instance initialized, so
            // it won; otherwise a replica stored its token in between, so the caller runs another attempt.
            var concurrent = await InstanceSetupRecords.FindAsync(
                StoreManagerFactory,
                cancellationToken
            );
            return concurrent is { IsInitialized: true } ? CreateAlreadySetUpResult() : null;
        }

        Logger.RootAdministratorCreated(tenantId, principalId);

        if (consoleClientId is not null)
            Logger.InstanceSetupClientRegistered(consoleClientId, tenantId);

        if (automationClientId is not null)
            Logger.InstanceSetupClientRegistered(automationClientId, tenantId);

        return new InstanceSetupResult
        {
            AutomationClientId = automationClientId,
            Root = new CreateRootResponse
            {
                TenantId = tenantId,
                PrincipalId = principalId,
                UserName = staging.UserName,
                DisplayName = displayName,
                ManagementConsoleClientId = consoleClientId,
                ManagementConsoleBaseUrl = consoleRegistration?.BaseUrl,
                Issuer = issuer,
                DiscoveryUrl = GetDiscoveryUrl(issuer),
                ManagementApiUrl = GetManagementApiUrl(issuer),
                ApiReferenceUrl = GetApiReferenceUrl(issuer, InstanceLinksOptions.ApiReferencePath),
            },
        };
    }
}
