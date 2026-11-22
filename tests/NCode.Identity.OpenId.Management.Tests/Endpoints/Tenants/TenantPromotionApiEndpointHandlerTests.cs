// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Promotions;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

public sealed class TenantPromotionApiEndpointHandlerTests : IDisposable
{
    private const string TeamId = "team-1";
    private const string Region = "region-1";
    private const string OtherRegion = "region-2";
    private const string ControlPlaneTenantId = "platform-root";
    private const string SourceTenantId = "source-tenant";
    private const string TargetTenantId = "target-tenant";
    private const string OtherTenantId = "other-tenant";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ITenantPromoter> MockTenantPromoter { get; }
    private Mock<ITenantConfigurationExporter> MockTenantConfigurationExporter { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private Mock<IMediator> MockMediator { get; }
    private TenantPromotionApiEndpointHandler Handler { get; }

    public TenantPromotionApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockTenantPromoter = MockRepository.Create<ITenantPromoter>();
        MockTenantConfigurationExporter = MockRepository.Create<ITenantConfigurationExporter>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        MockMediator = MockRepository.Create<IMediator>();

        Handler = CreateHandler(isControlPlane: true);
    }

    private TenantPromotionApiEndpointHandler CreateHandler(bool isControlPlane) =>
        new(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockAmbientTenantAccessor.Object,
            MockTenantPromoter.Object,
            MockTenantConfigurationExporter.Object,
            MockManagementAuditRecorder.Object,
            Options.Create(new CellOptions { IsControlPlane = isControlPlane, Region = Region })
        );

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();

        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();

        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupTargetStore(PersistedTenant? target)
    {
        SetupStoreManager();

        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TargetTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target)
            .Verifiable();
    }

    private void SetupSourceStore(PersistedTenant? source, PersistedTenant? target)
    {
        SetupTargetStore(target);

        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(SourceTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source)
            .Verifiable();
    }

    private void SetupSourceStore()
    {
        SetupSourceStore(CreateTenant(SourceTenantId), CreateTenant(TargetTenantId));
        SetupExport(CreateDocument());
    }

    private void SetupExport(TenantConfigurationDocument? document) =>
        MockTenantConfigurationExporter
            .Setup(x => x.ExportAsync(SourceTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document)
            .Verifiable();

    private static TenantConfigurationDocument CreateDocument(string region = Region) =>
        new()
        {
            SourceTenantId = SourceTenantId,
            Region = region,
            Settings = JsonElement.Parse("""{"a":1}"""),
            Clients = [],
            ClientGrants = [],
            ResourceServers = [],
        };

    private void SetupAuthorize(
        string tenantId,
        string resourceType,
        string resourceId,
        IAuthorizationRequirement requirement,
        AuthorizationResult result
    ) =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.Is<object?>(resource =>
                        resource is ResourceNode
                        && ((ResourceNode)resource).TenantId == tenantId
                        && ((ResourceNode)resource).ResourceType == resourceType
                        && ((ResourceNode)resource).ResourceId == resourceId
                    ),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                        requirements.Contains(requirement)
                    )
                )
            )
            .ReturnsAsync(result)
            .Verifiable();

    private void SetupTargetAuthorization(AuthorizationResult result) =>
        SetupAuthorize(
            TargetTenantId,
            ResourceNodeTypes.Tenant,
            TargetTenantId,
            Operations.Create,
            result
        );

    private void SetupSourceAuthorization(AuthorizationResult result) =>
        SetupAuthorize(
            SourceTenantId,
            ResourceNodeTypes.Tenant,
            SourceTenantId,
            Operations.Read,
            result
        );

    private void SetupRouteAuthorization(bool hasSourceTenant)
    {
        SetupTargetAuthorization(AuthorizationResult.Success());
        if (hasSourceTenant)
        {
            SetupSourceAuthorization(AuthorizationResult.Success());
        }
    }

    private void SetupPlan(Action<TenantPromotionContext> plan) =>
        MockTenantPromoter
            .Setup(x =>
                x.PlanAsync(It.IsAny<TenantPromotionContext>(), It.IsAny<CancellationToken>())
            )
            .Callback<TenantPromotionContext, CancellationToken>((context, _) => plan(context))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupApply() =>
        MockTenantPromoter
            .Setup(x =>
                x.ApplyAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<TenantPromotionContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private HttpContext CreateHttpContext(bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "test")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection()
                .AddSingleton(MockMediator.Object)
                .BuildServiceProvider(),
        };
        // The management caller gate publishes the context in the pipeline; emulate it for direct handler calls.
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Environment
                        == Mock.Of<OpenIdEnvironment>(environment =>
                            environment.JsonSerializerOptions == JsonSerializerOptions.Default
                        )
                    && context.Tenant
                        == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == ControlPlaneTenantId)
                )
            )
        );
        return httpContext;
    }

    private static PersistedTenant CreateTenant(string tenantId, string teamId = TeamId) =>
        new PersistedTenantBuilder
        {
            TenantId = tenantId,
            TeamId = teamId,
            Settings = JsonElement.Parse("""{"a":1}"""),
        }.Build();

    private static PromoteTenantRequest CreateSourceTenantRequest(
        string sourceTenantId = SourceTenantId
    ) => new() { SourceTenantId = sourceTenantId };

    private static PromoteTenantRequest CreateInlineRequest(
        TenantConfigurationDocument? document = null
    ) => new() { Source = document ?? CreateDocument() };

    private ValueTask<IResult> PreviewAsync(
        PromoteTenantRequest request,
        string tenantId = TargetTenantId,
        bool authenticated = true,
        CancellationToken cancellationToken = default
    ) =>
        Handler.PreviewPromotionAsync(
            CreateHttpContext(authenticated),
            tenantId,
            request,
            cancellationToken
        );

    private ValueTask<IResult> PromoteAsync(
        PromoteTenantRequest request,
        string tenantId = TargetTenantId,
        CancellationToken cancellationToken = default
    ) =>
        Handler.PromoteAsync(
            CreateHttpContext(authenticated: true),
            tenantId,
            request,
            cancellationToken
        );

    private static void AddChange(TenantPromotionContext context) =>
        context.AddChange(
            new TenantPromotionChangeResource
            {
                ResourceType = ManagementResourceTypes.Tenant,
                ResourceId = TargetTenantId,
                Action = TenantPromotionActions.Updated,
            }
        );

    private static void AddError(TenantPromotionContext context) =>
        context.AddError(
            new TenantPromotionErrorResource
            {
                ResourceType = ManagementResourceTypes.Client,
                ResourceId = "client-1",
                Detail = "The client cannot be promoted.",
            }
        );

    private static ResourceNode CreateOtherNode(string tenantId = OtherTenantId) =>
        ResourceNode.For(tenantId, ResourceNodeTypes.Tenant, tenantId);

    private static void AssertProblem(IResult result, int statusCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(statusCode, problem.StatusCode);
    }

    #endregion

    #region PreviewPromotionAsync Tests

    [Theory]
    [InlineData("", SourceTenantId, false)]
    [InlineData(" ", SourceTenantId, false)]
    [InlineData(TargetTenantId, SourceTenantId, true)]
    [InlineData(TargetTenantId, null, false)]
    [InlineData(TargetTenantId, " ", false)]
    [InlineData(TargetTenantId, TargetTenantId, false)]
    public async Task PreviewPromotionAsync_WhenTheRequestIsNotOneDistinctSource_ReturnsBadRequestBeforeAnyRead(
        string tenantId,
        string? sourceTenantId,
        bool hasDocument
    )
    {
        var request = new PromoteTenantRequest
        {
            SourceTenantId = sourceTenantId,
            Source = hasDocument ? CreateDocument() : null,
        };

        var result = await PreviewAsync(request, tenantId);

        AssertProblem(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheCallerMayNotCreateInTheTarget_ReturnsForbiddenBeforeAnyRead()
    {
        SetupTargetAuthorization(AuthorizationResult.Failed());

        // Strict mocks: no store factory setup, so reading before refusing would fail the test.
        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheCallerIsAnonymous_ReturnsUnauthorizedBeforeAnyRead()
    {
        SetupTargetAuthorization(AuthorizationResult.Failed());

        var result = await PreviewAsync(CreateSourceTenantRequest(), authenticated: false);

        AssertProblem(result, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheCallerMayNotReadTheSource_ReturnsForbiddenBeforeAnyRead()
    {
        SetupTargetAuthorization(AuthorizationResult.Success());
        SetupSourceAuthorization(AuthorizationResult.Failed());

        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheTargetDoesNotExist_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupTargetStore(target: null);

        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheTargetOfAnInlineDocumentDoesNotExist_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(target: null);

        var result = await PreviewAsync(CreateInlineRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheSourceDoesNotExist_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore(source: null, CreateTenant(TargetTenantId));

        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheSourceBelongsToAnotherTeam_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore(CreateTenant(SourceTenantId, "team-2"), CreateTenant(TargetTenantId));

        // Strict mocks: no export setup, so exporting another team's tenant would fail the test.
        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheSourceCannotBeExported_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore(CreateTenant(SourceTenantId), CreateTenant(TargetTenantId));
        SetupExport(document: null);

        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheInlineDocumentIsFromAnotherRegion_ReturnsUnprocessableEntityWithoutPlanning()
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(CreateTenant(TargetTenantId));

        // Strict mocks: no PlanAsync setup, so planning a document from another region would fail the test.
        var result = await PreviewAsync(CreateInlineRequest(CreateDocument(OtherRegion)));

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheTargetIsTheControlPlaneTenant_ReturnsUnprocessableEntityBeforeAnyRead()
    {
        SetupAuthorize(
            ControlPlaneTenantId,
            ResourceNodeTypes.Tenant,
            ControlPlaneTenantId,
            Operations.Create,
            AuthorizationResult.Success()
        );

        // Strict mocks: no store factory setup, so reading the control-plane tenant would fail the test.
        var result = await PreviewAsync(CreateInlineRequest(), ControlPlaneTenantId);

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity);
    }

    [Theory]
    [InlineData("cell-eu-1")]
    [InlineData("CELL-eu-1")]
    [InlineData("bad id")]
    public async Task PreviewPromotionAsync_WhenTheDocumentNamesAClientIdAPromotionDoesNotCreate_ReturnsUnprocessableEntityWithoutPlanning(
        string clientId
    )
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(CreateTenant(TargetTenantId));
        var document = CreateDocument();
        document.Clients.Add(
            new TenantConfigurationClient
            {
                ClientId = clientId,
                IsDisabled = false,
                ResourceServerId = null,
                Settings = JsonElement.Parse("""{"a":1}"""),
            }
        );

        // Strict mocks: no PlanAsync setup, so planning a document that names the identifier would fail the test.
        var result = await PreviewAsync(CreateInlineRequest(document));

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenPlannedFromASourceTenant_ReturnsTheReportWithoutApplying()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        TenantPromotionContext? planned = null;
        SetupPlan(context =>
        {
            planned = context;
            AddChange(context);
            AddError(context);
        });

        // Strict mocks: no ApplyAsync, audit or announcement setup, so a preview that writes would fail the test.
        var result = await PreviewAsync(CreateSourceTenantRequest());

        var ok = Assert.IsType<Ok<TenantPromotionResource>>(result);
        var report = ok.Value;
        Assert.NotNull(report);
        Assert.Equal(TeamId, report.TeamId);
        Assert.Equal(SourceTenantId, report.SourceTenantId);
        Assert.Equal(TargetTenantId, report.TargetTenantId);
        Assert.False(report.IsApplied);
        Assert.Single(report.Changes);
        Assert.Single(report.Errors);
        Assert.Empty(report.Notes);

        Assert.NotNull(planned);
        Assert.Equal(SourceTenantId, planned.Source.SourceTenantId);
        Assert.Equal(TargetTenantId, planned.TargetTenant.TenantId);
        Assert.Equal(
            (ControlPlaneTenantId, ResourceNodeTypes.Team, TeamId),
            (
                planned.TeamNode.TenantId,
                ((ResourceNode)planned.TeamNode).ResourceType,
                ((ResourceNode)planned.TeamNode).ResourceId
            )
        );
        Assert.Equal(
            (TargetTenantId, ResourceNodeTypes.Tenant, TargetTenantId),
            (
                planned.TargetTenantNode.TenantId,
                ((ResourceNode)planned.TargetTenantNode).ResourceType,
                ((ResourceNode)planned.TargetTenantNode).ResourceId
            )
        );
        Assert.True(
            JsonElement.DeepEquals(
                planned.TargetTenantSettings,
                CreateTenant(TargetTenantId).Settings.Value
            )
        );
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenPlannedFromAnInlineDocument_ReturnsTheReportWithoutReadingASourceTenant()
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(CreateTenant(TargetTenantId));
        var document = CreateDocument();
        TenantPromotionContext? planned = null;
        SetupPlan(context =>
        {
            planned = context;
            AddChange(context);
        });

        // Strict mocks: no source read and no export setup, so reading a source tenant would fail the test.
        var result = await PreviewAsync(CreateInlineRequest(document));

        var ok = Assert.IsType<Ok<TenantPromotionResource>>(result);
        var report = ok.Value;
        Assert.NotNull(report);
        Assert.Equal(TeamId, report.TeamId);
        Assert.Equal(SourceTenantId, report.SourceTenantId);
        Assert.Equal(TargetTenantId, report.TargetTenantId);
        Assert.False(report.IsApplied);

        Assert.NotNull(planned);
        Assert.Same(document, planned.Source);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenALimitChangesAndTheTeamRefuses_ReturnsForbiddenNamingTheLimit()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context =>
            context.AddChangedLimits(context.TeamNode, [OpenIdSettingNames.MaxTenants])
        );
        SetupAuthorize(
            ControlPlaneTenantId,
            ResourceNodeTypes.Team,
            TeamId,
            Operations.Update,
            AuthorizationResult.Failed()
        );

        // Strict mocks: no ApplyAsync setup, so a refused preview that applies would fail the test.
        var result = await PreviewAsync(CreateSourceTenantRequest());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Contains("'max_tenants'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenLimitsChangeAtOneNode_AuthorizesOnceNamingTheUnion()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context =>
        {
            context.AddChangedLimits(context.TeamNode, ["b", "a"]);
            context.AddChangedLimits(context.TeamNode, ["a", "c"]);
        });
        SetupAuthorize(
            ControlPlaneTenantId,
            ResourceNodeTypes.Team,
            TeamId,
            Operations.Update,
            AuthorizationResult.Failed()
        );

        var result = await PreviewAsync(CreateSourceTenantRequest());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Contains("'a', 'b', 'c'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheFirstNodeAuthorizesAndTheSecondRefuses_ReturnsForbiddenNamingTheSecondLimit()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context =>
        {
            context.AddChangedLimits(context.TeamNode, [OpenIdSettingNames.MaxTenants]);
            context.AddChangedLimits(
                context.TargetTenantNode,
                [OpenIdSettingNames.MaxConcurrentSessions]
            );
        });
        SetupAuthorize(
            ControlPlaneTenantId,
            ResourceNodeTypes.Team,
            TeamId,
            Operations.Update,
            AuthorizationResult.Success()
        );
        SetupAuthorize(
            TargetTenantId,
            ResourceNodeTypes.Tenant,
            TargetTenantId,
            Operations.Update,
            AuthorizationResult.Failed()
        );

        var result = await PreviewAsync(CreateSourceTenantRequest());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Contains(
            "'max_concurrent_sessions'",
            problem.ProblemDetails.Detail,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "'max_tenants'",
            problem.ProblemDetails.Detail,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenTheCallerMayNotGrantARoleTheClientHolds_ReturnsForbidden()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context => context.AddRequiredCreateAuthority(CreateOtherNode()));
        SetupAuthorize(
            OtherTenantId,
            ResourceNodeTypes.Tenant,
            OtherTenantId,
            Operations.Create,
            AuthorizationResult.Failed()
        );

        // Strict mocks: no ApplyAsync setup, so a refused preview that applies would fail the test.
        var result = await PreviewAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PreviewPromotionAsync_WhenNodesDifferOnlyByCase_AuthorizesTheNodeOnce()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context =>
        {
            context.AddRequiredCreateAuthority(CreateOtherNode());
            context.AddRequiredCreateAuthority(
                ResourceNode.For("OTHER-TENANT", "TENANT", "OTHER-TENANT")
            );
        });

        // Strict mocks: a second authorization for the upper-case node matches no setup, so it would fail the test.
        SetupAuthorize(
            OtherTenantId,
            ResourceNodeTypes.Tenant,
            OtherTenantId,
            Operations.Create,
            AuthorizationResult.Success()
        );

        var result = await PreviewAsync(CreateSourceTenantRequest());

        Assert.IsType<Ok<TenantPromotionResource>>(result);
    }

    #endregion

    #region PromoteAsync Tests

    [Theory]
    [InlineData(TargetTenantId, SourceTenantId, true)]
    [InlineData(TargetTenantId, null, false)]
    [InlineData(TargetTenantId, TargetTenantId, false)]
    public async Task PromoteAsync_WhenTheRequestIsNotOneDistinctSource_ReturnsBadRequestBeforeAnyRead(
        string tenantId,
        string? sourceTenantId,
        bool hasDocument
    )
    {
        var request = new PromoteTenantRequest
        {
            SourceTenantId = sourceTenantId,
            Source = hasDocument ? CreateDocument() : null,
        };

        var result = await PromoteAsync(request, tenantId);

        AssertProblem(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheCallerMayNotCreateInTheTarget_ReturnsForbiddenBeforeAnyRead()
    {
        SetupTargetAuthorization(AuthorizationResult.Failed());

        var result = await PromoteAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheCallerMayNotReadTheSource_ReturnsForbiddenBeforeAnyRead()
    {
        SetupTargetAuthorization(AuthorizationResult.Success());
        SetupSourceAuthorization(AuthorizationResult.Failed());

        var result = await PromoteAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheTargetDoesNotExist_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupTargetStore(target: null);

        var result = await PromoteAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheSourceBelongsToAnotherTeam_ReturnsNotFound()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore(CreateTenant(SourceTenantId, "team-2"), CreateTenant(TargetTenantId));

        var result = await PromoteAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheInlineDocumentIsFromAnotherRegion_ReturnsUnprocessableEntityWithoutApplying()
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(CreateTenant(TargetTenantId));

        // Strict mocks: no PlanAsync or ApplyAsync setup, so the promoter is never called.
        var result = await PromoteAsync(CreateInlineRequest(CreateDocument(OtherRegion)));

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task PromoteAsync_WhenALimitChangesAndTheTeamRefuses_ReturnsForbiddenWithoutApplying()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context =>
            context.AddChangedLimits(context.TeamNode, [OpenIdSettingNames.MaxTenants])
        );
        SetupAuthorize(
            ControlPlaneTenantId,
            ResourceNodeTypes.Team,
            TeamId,
            Operations.Update,
            AuthorizationResult.Failed()
        );

        // Strict mocks: no ApplyAsync setup, so applying a refused promotion would fail the test.
        var result = await PromoteAsync(CreateSourceTenantRequest());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Contains("'max_tenants'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromoteAsync_WhenTheCallerMayNotGrantARoleTheClientHolds_ReturnsForbiddenWithoutApplying()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(context => context.AddRequiredCreateAuthority(CreateOtherNode()));
        SetupAuthorize(
            OtherTenantId,
            ResourceNodeTypes.Tenant,
            OtherTenantId,
            Operations.Create,
            AuthorizationResult.Failed()
        );

        var result = await PromoteAsync(CreateSourceTenantRequest());

        AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PromoteAsync_WhenThePlanHasErrors_ReturnsConflictWithTheErrorsWithoutApplying()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(AddError);

        // Strict mocks: no ApplyAsync setup, so applying a promotion with errors would fail the test.
        var result = await PromoteAsync(CreateSourceTenantRequest());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<TenantPromotionErrorResource>>(
            problem.ProblemDetails.Extensions["errors"]
        );
        var error = Assert.Single(errors);
        Assert.Equal("client-1", error.ResourceId);
    }

    [Fact]
    public async Task PromoteAsync_WhenPlannedFromASourceTenant_AppliesRecordsTheAuditAndAnnouncesTheTarget()
    {
        SetupRouteAuthorization(hasSourceTenant: true);
        SetupSourceStore();
        SetupPlan(AddChange);
        SetupApply();
        var resourceValues = SetupAuditAndAnnouncement();

        var result = await PromoteAsync(CreateSourceTenantRequest());

        var ok = Assert.IsType<Ok<TenantPromotionResource>>(result);
        var report = ok.Value;
        Assert.NotNull(report);
        Assert.True(report.IsApplied);
        Assert.Equal(TeamId, report.TeamId);
        Assert.Equal(TargetTenantId, report.TargetTenantId);
        Assert.Single(report.Changes);

        Assert.NotNull(resourceValues.Value);
        Assert.True(resourceValues.Value.Value.GetProperty("isApplied").GetBoolean());
        Assert.Equal(
            TargetTenantId,
            resourceValues.Value.Value.GetProperty("targetTenantId").GetString()
        );
    }

    [Fact]
    public async Task PromoteAsync_WhenPlannedFromAnInlineDocument_AppliesRecordsTheAuditAndAnnouncesTheTarget()
    {
        SetupRouteAuthorization(hasSourceTenant: false);
        SetupTargetStore(CreateTenant(TargetTenantId));
        SetupPlan(AddChange);
        SetupApply();
        _ = SetupAuditAndAnnouncement();

        var result = await PromoteAsync(CreateInlineRequest());

        var ok = Assert.IsType<Ok<TenantPromotionResource>>(result);
        var report = ok.Value;
        Assert.NotNull(report);
        Assert.True(report.IsApplied);
        Assert.Equal(SourceTenantId, report.SourceTenantId);
        Assert.Equal(TargetTenantId, report.TargetTenantId);
    }

    private StrongBox<JsonElement?> SetupAuditAndAnnouncement()
    {
        var resourceValues = new StrongBox<JsonElement?>();
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TargetTenantId,
                    ResourceChangeTypes.Promoted,
                    It.IsAny<JsonElement?>()
                )
            )
            .Callback<HttpContext, string, string, string, JsonElement?>(
                (_, _, _, _, values) => resourceValues.Value = values
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    new TenantChangedCommand(TargetTenantId, true),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        return resourceValues;
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData(
        "POST",
        "/tenants/{tenantId}/promotions/preview",
        new[] { 400, 401, 403, 404, 415, 422 }
    )]
    [InlineData(
        "POST",
        "/tenants/{tenantId}/promotions",
        new[] { 400, 401, 403, 404, 409, 412, 415, 422 }
    )]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemARouteAnswers(
        string httpMethod,
        string routePattern,
        int[] statusCodes
    )
    {
        Assert.Equal(
            statusCodes,
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Map_WhetherOrNotTheCellIsTheControlPlane_MapsThePromotionRoutes(bool isControlPlane)
    {
        var routes = MapRoutes(CreateHandler(isControlPlane));

        Assert.Equal(
            ["/tenants/{tenantId}/promotions", "/tenants/{tenantId}/promotions/preview"],
            routes
        );
    }

    private static string[] MapRoutes(TenantPromotionApiEndpointHandler handler)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        handler.Map(app);

        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];
    }

    #endregion
}
