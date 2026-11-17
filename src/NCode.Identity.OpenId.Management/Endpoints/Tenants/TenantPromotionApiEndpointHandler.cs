// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Promotions;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/*

POST api/tenants/{tenantId}/promotions/preview
POST api/tenants/{tenantId}/promotions

*/

/// <summary>
/// Provides the API endpoints for promoting a tenant's configuration into the tenant named by the route, from a source
/// tenant of the same cell or from a configuration document exported by another cell of the same region: a preview that
/// reports what the promotion changes and writes nothing, and the promotion that applies it. Every cell maps the
/// routes.
/// </summary>
// See ADR-1425.
internal class TenantPromotionApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantPromoter tenantPromoter,
    ITenantConfigurationExporter tenantConfigurationExporter,
    IManagementAuditRecorder managementAuditRecorder,
    IOptions<CellOptions> cellOptions
) : BaseApiEndpointHandler, IEndpointProvider
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ITenantPromoter TenantPromoter { get; } = tenantPromoter;
    private ITenantConfigurationExporter TenantConfigurationExporter { get; } =
        tenantConfigurationExporter;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var promotions = endpoints
            .MapGroup("/tenants/{tenantId}/promotions")
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        promotions
            .MapPost("/preview", PreviewPromotionAsync)
            .Produces<TenantPromotionResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        promotions
            .MapPost("", PromoteAsync)
            .Produces<TenantPromotionResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>
    /// Previews the promotion of a source configuration into the tenant named by the route: what it would change, what
    /// it notes and what refuses it. A preview writes nothing.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the tenant that receives the configuration.</param>
    /// <param name="request">The source and the environment-bound settings the target keeps.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The report of the promotion, which is not applied.</response>
    /// <response code="400">The request names its source in neither or both of the two ways, or names the target as its source.</response>
    /// <response code="403">The caller lacks the authority the promotion needs.</response>
    /// <response code="404">A tenant does not exist, or the source is not a tenant of the target's team.</response>
    /// <response code="422">The target is the control-plane tenant, or the source document comes from another region or names a client identifier a promotion does not create.</response>
    [EndpointName("api/tenants/promotions/preview")]
    internal virtual async ValueTask<IResult> PreviewPromotionAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromBody] PromoteTenantRequest request,
        CancellationToken cancellationToken
    )
    {
        return await PlanPromotionAsync(
            httpContext,
            tenantId,
            request,
            (teamId, context, _) =>
                ValueTask.FromResult<IResult>(
                    TypedResults.Ok(ToReport(teamId, context, isApplied: false))
                ),
            cancellationToken
        );
    }

    /// <summary>
    /// Promotes a source configuration into the tenant named by the route. A promotion that has errors writes nothing.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the tenant that receives the configuration.</param>
    /// <param name="request">The source and the environment-bound settings the target keeps.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The report of the promotion, which is applied.</response>
    /// <response code="400">The request names its source in neither or both of the two ways, or names the target as its source.</response>
    /// <response code="403">The caller lacks the authority the promotion needs.</response>
    /// <response code="404">A tenant does not exist, or the source is not a tenant of the target's team.</response>
    /// <response code="409">The promotion has errors, so nothing was written.</response>
    /// <response code="412">Another change wrote the target first.</response>
    /// <response code="422">The target is the control-plane tenant, or the source document comes from another region or names a client identifier a promotion does not create.</response>
    [EndpointName("api/tenants/promotions/create")]
    internal virtual async ValueTask<IResult> PromoteAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromBody] PromoteTenantRequest request,
        CancellationToken cancellationToken
    )
    {
        return await PlanPromotionAsync(
            httpContext,
            tenantId,
            request,
            (teamId, context, token) => ApplyPromotionAsync(httpContext, teamId, context, token),
            cancellationToken
        );
    }

    // Applies a planned promotion that no authority check refused, unless the plan has errors, then records the audit
    // event and announces the target tenant.
    private async ValueTask<IResult> ApplyPromotionAsync(
        HttpContext httpContext,
        string teamId,
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.Errors.Count > 0)
        {
            return TypedResults.Problem(
                detail: "The promotion cannot be applied; preview it to see why.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["errors"] = context.Errors }
            );
        }

        await TenantPromoter.ApplyAsync(httpContext, context, cancellationToken);

        var report = ToReport(teamId, context, isApplied: true);

        await ManagementAuditRecorder.RecordTenantChangedAsync(
            httpContext,
            teamId,
            context.TargetTenant.TenantId,
            ResourceChangeTypes.Promoted,
            ToResourceValues(report)
        );

        await NotifyTenantChangedAsync(
            httpContext,
            context.TargetTenant.TenantId,
            settingsChanged: true,
            cancellationToken
        );

        return TypedResults.Ok(report);
    }

    // Refuses a request that names its source in neither or both of the two ways, or names the target as its source,
    // and a caller without authority at the tenants' nodes. It reads nothing, so a caller without authority learns
    // nothing about either tenant (ADR-1424).
    private async ValueTask<IResult?> GetRefusalBeforeReadingAsync(
        HttpContext httpContext,
        string tenantId,
        PromoteTenantRequest request,
        CancellationToken cancellationToken
    )
    {
        var hasSourceTenantId = !string.IsNullOrWhiteSpace(request.SourceTenantId);
        if (
            string.IsNullOrWhiteSpace(tenantId)
            || hasSourceTenantId == (request.Source is not null)
            || string.Equals(request.SourceTenantId, tenantId, StringComparison.Ordinal)
        )
        {
            return TypedResults.Problem(
                detail: "The promotion needs a target tenant and exactly one source, a different tenant or a configuration document.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var targetFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetTenantNode(tenantId),
            Operations.Create,
            cancellationToken
        );
        if (targetFailure is not null)
        {
            return targetFailure;
        }

        // The control-plane tenant holds the platform's machine clients and the cells' clients, which the directory and
        // the control plane trust by identifier, so no configuration is promoted into it. See ADR-1425 and ADR-1466.
        if (
            string.Equals(
                tenantId,
                httpContext.GetOpenIdContext().Tenant.TenantId,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return TypedResults.Problem(
                detail: "The control-plane tenant is not a promotion target.",
                statusCode: StatusCodes.Status422UnprocessableEntity
            );
        }

        if (!hasSourceTenantId)
        {
            return null;
        }

        return await GetAuthorizationFailureAsync(
            httpContext,
            GetTenantNode(request.SourceTenantId ?? string.Empty),
            Operations.Read,
            cancellationToken
        );
    }

    // Reads the source configuration document: the exported document of a source tenant of the target's team, or the
    // request's inline document. It returns a refusal when the source is missing, is not the team's, or comes from
    // another region.
    private async ValueTask<(
        TenantConfigurationDocument? Document,
        IResult? Refusal
    )> GetSourceAsync(
        ITenantStore tenantStore,
        string teamId,
        PromoteTenantRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Source is not null)
        {
            return string.Equals(
                request.Source.Region,
                CellOptions.Region,
                StringComparison.Ordinal
            )
                ? (request.Source, null)
                : (
                    null,
                    TypedResults.Problem(
                        detail: "The configuration document comes from another region, and a promotion runs within one region.",
                        statusCode: StatusCodes.Status422UnprocessableEntity
                    )
                );
        }

        var source = await tenantStore.GetOrDefaultAsync(
            request.SourceTenantId ?? string.Empty,
            cancellationToken
        );
        if (source is null || !string.Equals(source.TeamId, teamId, StringComparison.Ordinal))
        {
            return (null, ManagementProblems.ResourceNotFound());
        }

        var document = await TenantConfigurationExporter.ExportAsync(
            source.TenantId,
            cancellationToken
        );

        return document is null ? (null, ManagementProblems.ResourceNotFound()) : (document, null);
    }

    // Refuses before reading, reads the target tenant and the source document, plans the promotion, then refuses it
    // when the caller lacks the authority it needs: Update at the node above each changed limit (ADR-1418), then
    // Create at the owning node of each machine client whose grant it writes (ADR-1466). Only a promotion that no
    // check refused reaches completeAsync, while the unit of work is still open.
    private async ValueTask<IResult> PlanPromotionAsync(
        HttpContext httpContext,
        string tenantId,
        PromoteTenantRequest request,
        Func<string, TenantPromotionContext, CancellationToken, ValueTask<IResult>> completeAsync,
        CancellationToken cancellationToken
    )
    {
        var refusalBeforeReading = await GetRefusalBeforeReadingAsync(
            httpContext,
            tenantId,
            request,
            cancellationToken
        );
        if (refusalBeforeReading is not null)
        {
            return refusalBeforeReading;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenantStore = storeManager.GetStore<ITenantStore>();

        var target = await tenantStore.GetOrDefaultAsync(tenantId, cancellationToken);
        if (target is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        var teamId = target.TeamId;

        var (sourceDocument, sourceRefusal) = await GetSourceAsync(
            tenantStore,
            teamId,
            request,
            cancellationToken
        );
        if (sourceDocument is null)
        {
            return sourceRefusal ?? ManagementProblems.ResourceNotFound();
        }

        var clientIdRefusal = GetClientIdRefusalOrDefault(sourceDocument);
        if (clientIdRefusal is not null)
        {
            return clientIdRefusal;
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var context = new TenantPromotionContext
        {
            Source = sourceDocument,
            TargetTenant = target,
            TeamNode = GetTeamNode(httpContext, teamId),
            TargetTenantNode = GetTenantNode(target.TenantId),
            StoreManager = storeManager,
            AmbientTenantAccessor = AmbientTenantAccessor,
            JsonSerializerOptions = openIdContext.Environment.JsonSerializerOptions,
            EnvironmentSettings = request.EnvironmentSettings,
            TargetTenantSettings = target.Settings.Value,
        };

        await TenantPromoter.PlanAsync(context, cancellationToken);

        var limitRefusal = await GetPromotedLimitRefusalOrDefaultAsync(
            httpContext,
            context,
            cancellationToken
        );
        if (limitRefusal is not null)
        {
            return limitRefusal;
        }

        var custodyRefusal = await GetCustodyRefusalOrDefaultAsync(
            httpContext,
            context,
            cancellationToken
        );
        if (custodyRefusal is not null)
        {
            return custodyRefusal;
        }

        return await completeAsync(teamId, context, cancellationToken);
    }

    // A promotion creates a client under the identifier the document names, and an uploaded document is untrusted, so
    // every identifier meets the rule a requested one meets and stays out of the cells' namespace. See ADR-1466.
    private static ProblemHttpResult? GetClientIdRefusalOrDefault(
        TenantConfigurationDocument document
    )
    {
        var prefix = GlobalDirectoryProtocol.CellClientIdPrefix;

        foreach (var client in document.Clients)
        {
            if (
                ClientIdRules.IsValid(client.ClientId)
                && !client.ClientId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            return TypedResults.Problem(
                detail: $"The configuration document names the client '{client.ClientId}', whose identifier a promotion does not create: a client_id is 1 to {ClientIdRules.MaxLength} unreserved characters and does not start with '{prefix}'.",
                statusCode: StatusCodes.Status422UnprocessableEntity
            );
        }

        return null;
    }

    // Groups the changed limits by the node above their document, in the order they were recorded, and returns the first
    // refusal of a group the caller may not update.
    private async ValueTask<IResult?> GetPromotedLimitRefusalOrDefaultAsync(
        HttpContext httpContext,
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var groups = context.ChangedLimits.GroupBy<TenantPromotionLimitChange, ISupportTenantId>(
            x => x.AuthorityAbove,
            ReferenceEqualityComparer.Instance
        );

        foreach (var group in groups)
        {
            var names = group
                .SelectMany(x => x.SettingNames)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            var limitRefusal = await GetLimitChangeRefusalOrDefaultAsync(
                httpContext,
                names,
                group.Key,
                cancellationToken
            );
            if (limitRefusal is not null)
            {
                return limitRefusal;
            }
        }

        return null;
    }

    // A grant to a target machine client widens what its principal may do, so the caller needs Create at the node that
    // owns the client.
    private async ValueTask<IResult?> GetCustodyRefusalOrDefaultAsync(
        HttpContext httpContext,
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var checkedNodes = new List<(string TenantId, string ResourceType, string ResourceId)>();

        foreach (var node in context.RequiredCreateAuthorities)
        {
            var key = node is IResourceNode resourceNode
                ? (resourceNode.TenantId, resourceNode.ResourceType, resourceNode.ResourceId)
                : (node.TenantId, string.Empty, string.Empty);

            var isChecked = checkedNodes.Exists(x =>
                string.Equals(x.TenantId, key.Item1, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ResourceType, key.Item2, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ResourceId, key.Item3, StringComparison.OrdinalIgnoreCase)
            );
            if (isChecked)
            {
                continue;
            }

            checkedNodes.Add(key);

            var failure = await GetAuthorizationFailureAsync(
                httpContext,
                node,
                Operations.Create,
                cancellationToken
            );
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private static TenantPromotionResource ToReport(
        string teamId,
        TenantPromotionContext context,
        bool isApplied
    ) =>
        new()
        {
            TeamId = teamId,
            SourceTenantId = context.Source.SourceTenantId,
            TargetTenantId = context.TargetTenant.TenantId,
            IsApplied = isApplied,
            Changes = [.. context.Changes],
            Notes = [.. context.Notes],
            Errors = [.. context.Errors],
        };
}
