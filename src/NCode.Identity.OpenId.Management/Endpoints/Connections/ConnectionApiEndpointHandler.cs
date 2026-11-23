// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
    ICryptoService cryptoService,
    IManagementAuditRecorder managementAuditRecorder,
    ILogger<ConnectionApiEndpointHandler> logger
) : BaseApiEndpointHandler, IEndpointProvider
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IConnectionValidator ConnectionValidator { get; } = connectionValidator;
    private IEnumerable<IConnectionDependentSource> DependentSources { get; } = dependentSources;
    private ISettingSerializer SettingSerializer { get; } = settingSerializer;
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
    }

    /// <summary>
    /// Gets the location of a connection, which the create route answers with.
    /// </summary>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <returns>The location of the connection.</returns>
    private static string GetConnectionLocation(string tenantId, string connectionId) =>
        $"/tenants/{tenantId}/connections/{connectionId}";

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
            IsEnabled = request.IsEnabled,
            Options = request.Options ?? JsonElements.EmptyObject,
            Settings = new PersistedConnectionSettings
            {
                TenantId = tenantId,
                ConnectionId = connectionId,
                Value = settingsElement,
            },
        };

        var error = await ConnectionValidator.ValidateCreateAsync(
            connection,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return ToErrorResult(error);
        }

        // A connection carries only the settings that apply within it (ADR-1149): a settings document that cannot be
        // honored is rejected here rather than stored and failed when the connection's settings are next loaded.
        var settingErrors = SettingSerializer.ValidateSettings(
            settingsElement,
            OpenIdSettingScopes.Connection,
            openIdContext.Environment.JsonSerializerOptions
        );
        if (settingErrors.Count > 0)
        {
            return SettingsValidationProblem(settingErrors);
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
    /// <response code="409">The connection still holds identities or local accounts.</response>
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
}
