// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Connections;
using NCode.Identity.OpenId.Management.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Settings;
using SystemTextJsonPatch;
using SystemTextJsonPatch.Exceptions;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

/*

GET    api/tenants/{tenantId}/connections
POST   api/tenants/{tenantId}/connections
GET    api/tenants/{tenantId}/connections/{connectionId}
PATCH  api/tenants/{tenantId}/connections/{connectionId}
DELETE api/tenants/{tenantId}/connections/{connectionId}
GET    api/tenants/{tenantId}/connections/{connectionId}/settings
GET    api/tenants/{tenantId}/connections/{connectionId}/settings/limits
PATCH  api/tenants/{tenantId}/connections/{connectionId}/settings
GET    api/tenants/{tenantId}/connections/{connectionId}/effective-settings

*/

/// <summary>
/// Provides the API endpoints for managing a tenant's connections: the named, tenant-scoped resources of a registered
/// strategy through which end users sign in.
/// </summary>
// See ADR-1535.
internal class ConnectionApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    IConnectionValidator connectionValidator,
    IEnumerable<IConnectionDependentSource> dependentSources,
    ISettingSerializer settingSerializer,
    ISettingScopeCollectionProvider settingScopeCollectionProvider,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    ICryptoService cryptoService,
    IManagementAuditRecorder managementAuditRecorder,
    ILogger<ConnectionApiEndpointHandler> logger
) : BaseApiEndpointHandler, IEndpointProvider
{
    /// <summary>
    /// Gets the location of a connection, which the create route answers with.
    /// </summary>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <returns>The location of the connection.</returns>
    private static string GetConnectionLocation(string tenantId, string connectionId) =>
        $"/tenants/{tenantId}/connections/{connectionId}";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IConnectionValidator ConnectionValidator { get; } = connectionValidator;
    private ImmutableArray<IConnectionDependentSource> DependentSources { get; } =
    [.. dependentSources];
    private ISettingSerializer SettingSerializer { get; } = settingSerializer;

    private ISettingScopeCollectionProvider SettingScopeCollectionProvider { get; } =
        settingScopeCollectionProvider;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private ILogger Logger { get; } = logger;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var connections = endpoints
            .MapGroup("/connections")
            .WithTags(OpenIdConstants.EndpointTags.Connections);

        connections
            .MapGet("", ListAsync)
            .Produces<CollectionResource<ConnectionResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        connections
            .MapPost("", CreateAsync)
            .Produces<ConnectionResource>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        connections
            .MapGet("/{connectionId}", GetAsync)
            .Produces<ConnectionResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        connections
            .MapPatch("/{connectionId}", UpdateAsync)
            .Produces<ConnectionResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        connections
            .MapDelete("/{connectionId}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        connections
            .MapGet("/{connectionId}/settings", GetSettingsAsync)
            .Produces<ConnectionSettingsResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        connections
            .MapGet("/{connectionId}/settings/limits", GetSettingLimitsAsync)
            .Produces<SettingLimitsResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        connections
            .MapPatch("/{connectionId}/settings", UpdateSettingsAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        connections
            .MapGet("/{connectionId}/effective-settings", GetEffectiveSettingsAsync)
            .Produces<ConnectionEffectiveSettingsResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Maps a <see cref="PersistedConnection"/> to its <see cref="ConnectionResource"/> representation.
    /// </summary>
    /// <param name="connection">The <see cref="PersistedConnection"/> to map.</param>
    /// <returns>The mapped <see cref="ConnectionResource"/>.</returns>
    internal virtual ConnectionResource ToConnectionResource(PersistedConnection connection) =>
        new()
        {
            TenantId = connection.TenantId,
            ConnectionId = connection.ConnectionId,
            ConcurrencyToken = connection.ConcurrencyToken,
            Name = connection.Name,
            DisplayName = connection.DisplayName,
            Strategy = connection.Strategy,
            IsSystem = connection.IsSystem,
            IsEnabled = connection.IsEnabled,
            Options = connection.Options,
        };

    /// <summary>
    /// Lists the connections of a tenant.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="cursor">The opaque continuation token from a previous page, or <c>null</c> for the first
    /// page.</param>
    /// <param name="limit">The maximum number of connections to return on the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">A page of connections.</response>
    [EndpointName("api/connections/list")]
    internal virtual async ValueTask<IResult> ListAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    )
    {
        var effectiveLimit = NormalizeLimit(limit);

        var tenantScope = TenantScopeResource.For(tenantId);
        return await ProcessListAsync(
            httpContext,
            tenantScope,
            async () =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(
                    cancellationToken
                );
                var store = storeManager.GetStore<IConnectionStore>();
                var pageRequest = new PageRequest { Cursor = cursor, Limit = effectiveLimit };
                return await store.GetPageAsync(pageRequest, cancellationToken);
            },
            ToConnectionResource,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a connection.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="request">The request that describes the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="201">The created connection.</response>
    /// <response code="400">The name, display name, strategy or options are not valid, or the settings are not valid
    /// for a connection.</response>
    /// <response code="404">The tenant does not exist.</response>
    /// <response code="409">A connection with the same name already exists.</response>
    [EndpointName("api/connections/create")]
    internal virtual async ValueTask<IResult> CreateAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromBody] CreateConnectionRequest request,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Create,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var openIdContext = httpContext.GetOpenIdContext();

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        // The caller holds authority at the tenant's node, but the tenant may not exist: the store's insert would fail
        // on it, so the absent tenant is answered 404 before anything is validated or stored.
        var tenantNotFound = await GetTenantNotFoundOrDefaultAsync(
            storeManager,
            tenantId,
            cancellationToken
        );
        if (tenantNotFound is not null)
        {
            return tenantNotFound;
        }

        var connectionId = CryptoService.GenerateResourceId();
        var requestedSettings = request.Settings ?? default;
        var settingsObject = ToJsonObject(requestedSettings);
        var settingsElement = SerializeToElement(settingsObject);
        var connection = new PersistedConnection
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            ConcurrencyToken = string.Empty,
            Name = request.Name,
            Strategy = request.Strategy,
            DisplayName = request.DisplayName,
            IsSystem = false,
            IsEnabled = request.IsEnabled,
            Options = request.Options ?? JsonElements.EmptyObject,
            Settings = new PersistedConnectionSettings
            {
                TenantId = tenantId,
                ConnectionId = connectionId,
                Value = settingsElement,
            },
        };

        // A connection carries only the settings that apply within it (ADR-1149): a settings document that cannot be
        // honored is rejected here rather than stored and failed when the connection's settings are next loaded. It is
        // checked before the name's availability, so a taken name never masks a 400.
        var settingErrors = SettingSerializer.ValidateSettings(
            requestedSettings,
            OpenIdSettingScopes.Connection,
            openIdContext.Environment.JsonSerializerOptions
        );
        if (settingErrors.Count > 0)
        {
            return SettingsValidationProblem(settingErrors);
        }

        var error = await ConnectionValidator.ValidateCreateAsync(
            connection,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return ToErrorResult(error);
        }

        await store.AddAsync(connection, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var connectionResource = ToConnectionResource(connection);
        var connectionValues = ToResourceValues(connectionResource);
        await ManagementAuditRecorder.RecordConnectionChangedAsync(
            httpContext,
            tenantId,
            connectionId,
            ResourceChangeTypes.Created,
            connectionValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(
            connection.ConcurrencyToken
        );
        var location = GetConnectionLocation(tenantId, connectionId);
        return TypedResults.Created(location, connectionResource);
    }

    /// <summary>
    /// Gets a connection.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The requested connection.</response>
    /// <response code="404">The connection does not exist.</response>
    [EndpointName("api/connections/get")]
    internal virtual async ValueTask<IResult> GetAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantScope,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var store = storeManager.GetStore<IConnectionStore>();
                return await store.GetOrDefaultAsync(connectionId, token);
            },
            connection =>
            {
                // The stored connection carries its concurrency token without the interface that emits the entity tag.
                httpContext.Response.Headers.ETag = ManagementEntityTags.Format(
                    connection.ConcurrencyToken
                );
                return ToConnectionResource(connection);
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Updates a connection's display name, enabled flag and options.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="request">The JSON Patch document that modifies the connection.</param>
    /// <param name="ifMatch">The <c>If-Match</c> header that names the entity tag the update applies to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The updated connection.</response>
    /// <response code="400">The JSON Patch document cannot be applied, or the patched connection is invalid as for a
    /// create.</response>
    /// <response code="404">The connection does not exist.</response>
    /// <response code="412">The supplied <c>If-Match</c> header does not match the current connection's entity tag, or
    /// is malformed.</response>
    [EndpointName("api/connections/update")]
    internal virtual async ValueTask<IResult> UpdateAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromBody] JsonPatchDocument<UpdateConnectionRequest> request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Update,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connection = await store.GetOrDefaultAsync(connectionId, cancellationToken);
        if (connection is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var preconditionError = ManagementEntityTags.GetIfMatchErrorOrDefault(
            ifMatch,
            connection.ConcurrencyToken,
            "connection"
        );
        if (preconditionError is not null)
        {
            return ToErrorResult(preconditionError);
        }

        var previousOptions = connection.Options;
        var model = new UpdateConnectionRequest
        {
            DisplayName = connection.DisplayName,
            IsEnabled = connection.IsEnabled,
            Options = connection.Options,
        };

        try
        {
            request.ApplyTo(model);
        }
        catch (JsonPatchException exception)
        {
            Logger.JsonPatchFailed(exception);
            return TypedResults.Problem(
                detail: "The JSON Patch document could not be applied.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        connection.DisplayName = model.DisplayName;
        connection.IsEnabled = model.IsEnabled;
        connection.Options = model.Options;

        var error = await ConnectionValidator.ValidateUpdateAsync(
            connection,
            previousOptions,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return ToErrorResult(error);
        }

        // A lost race throws StoreConcurrencyException, which the route group answers with 412 (ADR-1012).
        await store.UpdateAsync(connection, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        // The save gives the stored connection a fresh concurrency token, which the connection read before the save
        // lacks.
        var updated = await store.GetOrDefaultAsync(connectionId, cancellationToken);
        if (updated is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var updatedResource = ToConnectionResource(updated);
        var updatedValues = ToResourceValues(updatedResource);
        await ManagementAuditRecorder.RecordConnectionChangedAsync(
            httpContext,
            tenantId,
            connectionId,
            ResourceChangeTypes.Updated,
            updatedValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(updated.ConcurrencyToken);
        return TypedResults.Json(updatedResource);
    }

    /// <summary>
    /// Deletes a connection with its settings.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The connection was deleted.</response>
    /// <response code="404">The connection does not exist.</response>
    /// <response code="409">The connection is a reserved system connection, or still holds identities or local
    /// accounts.</response>
    [EndpointName("api/connections/delete")]
    internal virtual async ValueTask<IResult> DeleteAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Delete,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connection = await store.GetOrDefaultAsync(connectionId, cancellationToken);
        if (connection is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        if (connection.IsSystem)
        {
            return TypedResults.Problem(
                detail: "A reserved system connection cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        foreach (var source in DependentSources)
        {
            var hasDependents = await source.HasDependentsAsync(
                storeManager,
                connection,
                cancellationToken
            );
            if (hasDependents)
            {
                return TypedResults.Problem(
                    detail: "The connection still holds identities or local accounts.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }
        }

        var connectionResource = ToConnectionResource(connection);
        var connectionValues = ToResourceValues(connectionResource);

        await store.RemoveAsync(connectionId, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await ManagementAuditRecorder.RecordConnectionChangedAsync(
            httpContext,
            tenantId,
            connectionId,
            ResourceChangeTypes.Deleted,
            connectionValues
        );

        return TypedResults.NoContent();
    }

    // Settings

    /// <summary>
    /// Maps a <see cref="PersistedConnectionSettings"/> to its <see cref="ConnectionSettingsResource"/> representation.
    /// </summary>
    /// <param name="settings">The <see cref="PersistedConnectionSettings"/> to map.</param>
    /// <returns>The mapped <see cref="ConnectionSettingsResource"/>.</returns>
    internal virtual ConnectionSettingsResource ToConnectionSettingsResource(
        PersistedConnectionSettings settings
    ) =>
        new()
        {
            TenantId = settings.TenantId,
            ConnectionId = settings.ConnectionId,
            ConcurrencyToken = settings.ConcurrencyToken,
            Settings = settings.Value,
        };

    /// <summary>
    /// Maps a connection and its effective settings to their <see cref="ConnectionEffectiveSettingsResource"/>
    /// representation, which lists only the settings that apply within a connection.
    /// </summary>
    /// <param name="connection">The <see cref="PersistedConnection"/> the settings belong to.</param>
    /// <param name="settings">The effective settings of the connection.</param>
    /// <returns>The mapped <see cref="ConnectionEffectiveSettingsResource"/>.</returns>
    internal virtual ConnectionEffectiveSettingsResource ToConnectionEffectiveSettingsResource(
        PersistedConnection connection,
        IReadOnlySettingCollection settings
    ) =>
        new()
        {
            TenantId = connection.TenantId,
            ConnectionId = connection.ConnectionId,
            Settings = ToEffectiveSettingsElement(
                settings,
                SettingScopeCollectionProvider.Collection,
                OpenIdSettingScopes.Connection
            ),
        };

    /// <summary>
    /// Merges a connection's persisted settings onto the addressed tenant's effective settings, producing the same
    /// effective <see cref="IReadOnlySettingCollection"/> that a sign-in through the connection reads: the team's
    /// effective settings, the tenant's and then the connection's persisted overrides. A document that cannot be
    /// honored fails naming its owner.
    /// </summary>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connection">The connection whose settings are merged.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> that reads the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The effective settings, or <c>null</c> when the tenant does not exist.</returns>
    /// <exception cref="UnreadableSettingsException">The connection's document, the tenant's or its team's cannot be
    /// honored.</exception>
    // See ADR-1149, ADR-1204 and ADR-1245.
    internal virtual async ValueTask<IReadOnlySettingCollection?> MergeConnectionSettingsAsync(
        string tenantId,
        PersistedConnection connection,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var teamEffective = await EffectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        var tenantEffective = EffectiveSettingsResolver.ResolveStored(
            teamEffective,
            OpenIdSettingScopes.Tenant,
            tenantId,
            tenant.Settings.Value
        );
        return EffectiveSettingsResolver.ResolveStored(
            tenantEffective,
            OpenIdSettingScopes.Connection,
            connection.ConnectionId,
            connection.Settings.Value
        );
    }

    /// <summary>
    /// Gets a connection's persisted settings.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The connection's settings.</response>
    /// <response code="404">The connection does not exist.</response>
    [EndpointName("api/connections/settings/get")]
    internal virtual async ValueTask<IResult> GetSettingsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        return await ProcessGetAsync(
            httpContext,
            tenantScope,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var store = storeManager.GetStore<IConnectionStore>();
                var connection = await store.GetOrDefaultAsync(connectionId, token);
                return connection?.Settings;
            },
            ToConnectionSettingsResource,
            cancellationToken
        );
    }

    /// <summary>
    /// Gets the limits a connection's settings carry and whether the caller may set them: whether it holds update
    /// authority over the connection's tenant.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The connection's setting limits.</response>
    /// <response code="404">The connection does not exist.</response>
    // See ADR-1418.
    [EndpointName("api/connections/settings/limits/get")]
    internal virtual async ValueTask<IResult> GetSettingLimitsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        CancellationToken cancellationToken
    )
    {
        var tenantNode = ResourceNode.For(tenantId, ResourceNodeTypes.Tenant, tenantId);

        async ValueTask<ResourceNode?> ReadNodeAboveAsync(CancellationToken token)
        {
            await using var storeManager = await StoreManagerFactory.CreateAsync(token);
            var store = storeManager.GetStore<IConnectionStore>();
            var connection = await store.GetOrDefaultAsync(connectionId, token);
            return connection is null ? null : tenantNode;
        }

        var limitNames = SettingSerializer.GetLimits(OpenIdSettingScopes.Connection);

        return await ProcessGetSettingLimitsAsync(
            httpContext,
            tenantNode,
            limitNames,
            ReadNodeAboveAsync,
            cancellationToken
        );
    }

    /// <summary>
    /// Updates a connection's persisted settings via a JSON Patch document.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="request">The JSON Patch document that modifies the settings.</param>
    /// <param name="ifMatch">The <c>If-Match</c> header that names the entity tag the update applies to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The settings were updated.</response>
    /// <response code="400">The JSON Patch document cannot be applied to the settings, or the patched settings are not valid for a connection.</response>
    /// <response code="404">The connection does not exist.</response>
    /// <response code="412">The supplied <c>If-Match</c> header does not match the current settings' entity tag, or is malformed.</response>
    [EndpointName("api/connections/settings/update")]
    internal virtual async ValueTask<IResult> UpdateSettingsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        [FromBody] JsonPatchDocument<JsonObject> request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            tenantScope,
            Operations.Update,
            cancellationToken
        );
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connection = await store.GetOrDefaultAsync(connectionId, cancellationToken);
        var settings = connection?.Settings;
        if (settings is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var preconditionError = ManagementEntityTags.GetIfMatchErrorOrDefault(
            ifMatch,
            settings.ConcurrencyToken,
            "settings"
        );
        if (preconditionError is not null)
        {
            return ToErrorResult(preconditionError);
        }

        var jsonObject = ToJsonObject(settings.Value);

        try
        {
            request.ApplyTo(jsonObject);
        }
        catch (JsonPatchException exception)
        {
            Logger.JsonPatchFailed(exception);
            return TypedResults.Problem(
                detail: "The JSON Patch document could not be applied.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var settingsValue = SerializeToElement(jsonObject);
        var jsonOptions = httpContext.GetOpenIdContext().Environment.JsonSerializerOptions;
        var settingErrors = SettingSerializer.ValidateSettings(
            settingsValue,
            OpenIdSettingScopes.Connection,
            jsonOptions
        );
        if (settingErrors.Count > 0)
        {
            return SettingsValidationProblem(settingErrors);
        }

        var changedLimits = SettingSerializer.GetChangedLimits(settings.Value, settingsValue);
        var authorityAbove = ResourceNode.For(tenantId, ResourceNodeTypes.Tenant, tenantId);
        var limitRefusal = await GetLimitChangeRefusalOrDefaultAsync(
            httpContext,
            changedLimits,
            authorityAbove,
            cancellationToken
        );
        if (limitRefusal is not null)
        {
            return limitRefusal;
        }

        settings.Value = settingsValue;
        await store.UpdateSettingsAsync(settings, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var settingsResource = ToConnectionSettingsResource(settings);
        var settingsValues = ToResourceValues(settingsResource);
        await ManagementAuditRecorder.RecordConnectionChangedAsync(
            httpContext,
            tenantId,
            connectionId,
            ResourceChangeTypes.SettingsUpdated,
            settingsValues
        );

        httpContext.Response.Headers.ETag = ManagementEntityTags.Format(settings.ConcurrencyToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Gets a connection's effective (merged) settings.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The connection's effective settings.</response>
    /// <response code="404">The connection or its tenant does not exist.</response>
    [EndpointName("api/connections/effective-settings/get")]
    internal virtual async ValueTask<IResult> GetEffectiveSettingsAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromRoute] string connectionId,
        CancellationToken cancellationToken
    )
    {
        var tenantScope = TenantScopeResource.For(tenantId);

        // The settings are resolved only after authorization, so a caller who may not read them learns nothing about a
        // document that cannot be honored. A null read is the absent connection or the absent tenant, answered 404.
        // See ADR-1245.
        return await ProcessGetAsync(
            httpContext,
            tenantScope,
            Operations.Read,
            async token =>
            {
                await using var storeManager = await StoreManagerFactory.CreateAsync(token);
                var store = storeManager.GetStore<IConnectionStore>();

                var connection = await store.GetOrDefaultAsync(connectionId, token);
                if (connection is null)
                {
                    return null;
                }

                var effectiveSettings = await MergeConnectionSettingsAsync(
                    tenantId,
                    connection,
                    storeManager,
                    token
                );
                return effectiveSettings is null
                    ? null
                    : ToConnectionEffectiveSettingsResource(connection, effectiveSettings);
            },
            resource => resource,
            cancellationToken
        );
    }
}
