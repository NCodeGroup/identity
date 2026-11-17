// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.EncryptionKeys;
using NCode.Identity.OpenId.Management.Contracts.Teams;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Auditing;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Settings;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints.Teams;

public sealed class TeamRootKeyApiEndpointHandlerTests : IDisposable
{
    private const string TeamId = "team-1";
    private const string ControlPlaneTenantId = "platform-root";
    private const string OwnRegion = "us-west";
    private const string OtherRegion = "eu-west";
    private const string RootKeyReference = "arn:aws:kms:us-west-2:111122223333:key/abcd";

    private static SecretOwner TeamOwner { get; } = new(SecretOwnerTypes.Team, TeamId);

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ITeamValidator> MockTeamValidator { get; }
    private Mock<ITeamDirectory> MockTeamDirectory { get; }
    private Mock<IOwnerRootKeyManager> MockOwnerRootKeyManager { get; }
    private Mock<ICellDirectory> MockCellDirectory { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<ISettingSerializer> MockSettingSerializer { get; }
    private Mock<ISettingScopeCollectionProvider> MockSettingScopeCollectionProvider { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IClientCoherenceValidator> MockClientCoherenceValidator { get; }
    private Mock<ITeamClientCoherenceChecker> MockTeamClientCoherenceChecker { get; }
    private Mock<IResourceOwnershipService> MockResourceOwnershipService { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private Mock<IMediator> MockMediator { get; }
    private TeamApiEndpointHandler Handler { get; }

    public TeamRootKeyApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTeamStore = MockRepository.Create<ITeamStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockTeamValidator = MockRepository.Create<ITeamValidator>();
        MockTeamDirectory = MockRepository.Create<ITeamDirectory>();
        MockOwnerRootKeyManager = MockRepository.Create<IOwnerRootKeyManager>();
        MockCellDirectory = MockRepository.Create<ICellDirectory>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockSettingSerializer = MockRepository.Create<ISettingSerializer>();
        MockSettingScopeCollectionProvider =
            MockRepository.Create<ISettingScopeCollectionProvider>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        MockClientCoherenceValidator = MockRepository.Create<IClientCoherenceValidator>();
        MockTeamClientCoherenceChecker = MockRepository.Create<ITeamClientCoherenceChecker>();
        MockResourceOwnershipService = MockRepository.Create<IResourceOwnershipService>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        MockMediator = MockRepository.Create<IMediator>();

        Handler = CreateHandler(isControlPlane: true);
    }

    private TeamApiEndpointHandler CreateHandler(bool isControlPlane) =>
        new(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockTeamValidator.Object,
            MockTeamDirectory.Object,
            MockOwnerRootKeyManager.Object,
            MockCellDirectory.Object,
            TimeProvider.System,
            MockCryptoService.Object,
            MockSettingSerializer.Object,
            MockSettingScopeCollectionProvider.Object,
            MockEffectiveSettingsResolver.Object,
            MockClientCoherenceValidator.Object,
            MockTeamClientCoherenceChecker.Object,
            MockResourceOwnershipService.Object,
            MockManagementAuditRecorder.Object,
            NullLogger<TeamApiEndpointHandler>.Instance,
            Options.Create(new CellOptions { IsControlPlane = isControlPlane, Region = OwnRegion })
        );

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private void SetupStore(PersistedTeam? team)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();

        MockStoreManager
            .Setup(x => x.GetStore<ITeamStore>())
            .Returns(MockTeamStore.Object)
            .Verifiable();

        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team)
            .Verifiable();
    }

    private void SetupAuthorization(IAuthorizationRequirement operation) =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                        requirements.Contains(operation)
                    )
                )
            )
            .ReturnsAsync(AuthorizationResult.Success())
            .Verifiable();

    private void SetupAuthorizationFailure(
        IAuthorizationRequirement operation,
        Action<object?> capture
    ) =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                        requirements.Contains(operation)
                    )
                )
            )
            .Callback(
                (ClaimsPrincipal _, object? resource, IEnumerable<IAuthorizationRequirement> _) =>
                    capture(resource)
            )
            .ReturnsAsync(AuthorizationResult.Failed())
            .Verifiable();

    private void SetupCanBringRootKey() =>
        MockOwnerRootKeyManager
            .Setup(x => x.CanBringRootKey(TeamOwner, out It.Ref<string?>.IsAny))
            .Returns(true)
            .Verifiable();

    private void SetupValidRequest()
    {
        MockOwnerRootKeyManager
            .SetupGet(x => x.AllowedKeyWrapperNames)
            .Returns(["aws-kms"])
            .Verifiable();
        MockOwnerRootKeyManager
            .Setup(x =>
                x.TryValidateKeyReference("aws-kms", RootKeyReference, out It.Ref<string?>.IsAny)
            )
            .Returns(true)
            .Verifiable();
    }

    private void SetupCells()
    {
        IReadOnlyList<CellDescriptor> cells =
        [
            CreateCell("cell-a", OwnRegion),
            CreateCell("cell-b", OtherRegion),
        ];
        MockCellDirectory
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<CellDescriptor>>(cells))
            .Verifiable();
    }

    private void SetupWrite() =>
        MockTeamStore
            .Setup(x =>
                x.UpdateRootKeysAsync(
                    It.Is<PersistedTeam>(team => team.TeamId == TeamId),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupSave() =>
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupPublish(Action<TeamDocument> capture) =>
        MockTeamDirectory
            .Setup(x =>
                x.PublishAsync(
                    It.Is<TeamDocument>(document => document.TeamId == TeamId),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback((TeamDocument document, CancellationToken _) => capture(document))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupPublishUnavailable() =>
        MockTeamDirectory
            .Setup(x => x.PublishAsync(It.IsAny<TeamDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory is down."))
            .Verifiable();

    private void SetupTeamChangedAudit() =>
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTeamChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    ResourceChangeTypes.Updated,
                    It.IsAny<JsonElement?>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupTeamChangedAnnouncement() =>
        MockMediator
            .Setup(x =>
                x.SendAsync(new TeamChangedCommand(TeamId, false), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupWritePublishAuditAndAnnounce(Action<TeamDocument> capture)
    {
        SetupWrite();
        SetupSave();
        SetupPublish(capture);
        SetupTeamChangedAudit();
        SetupTeamChangedAnnouncement();
    }

    private void SetupSetAsync()
    {
        var rootKey = CreateRootKeyInfo();
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest("aws-kms", RootKeyReference),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                new ValueTask<OwnerRootKeyChange>(
                    new OwnerRootKeyChange(
                        rootKey,
                        null,
                        null,
                        new MasterKeyRewrapResult(1, 0, 0, 0)
                    )
                )
            )
            .Verifiable();
    }

    private static CellDescriptor CreateCell(string name, string region) =>
        new()
        {
            Name = name,
            Region = region,
            Kind = CellKinds.Shared,
            ManagementUrl = $"https://{name}.example.test/",
        };

    private static OwnerRootKeyInfo CreateRootKeyInfo(string state = OwnerRootKeyStates.Active) =>
        new(
            TeamOwner,
            "aws-kms",
            RootKeyReference,
            state,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(1),
            DateTimeOffset.UnixEpoch.AddHours(2),
            null,
            null
        );

    private static EncryptionRootKeyRequest CreateRequest(string keyReference = RootKeyReference) =>
        new() { Provider = "aws-kms", KeyReference = keyReference };

    private static PersistedTeam CreateTeam(
        params (string Region, OwnerRootKeyRequest RootKey)[] rootKeys
    )
    {
        var team = new PersistedTeamBuilder
        {
            TeamId = TeamId,
            ConcurrencyToken = "team-ct",
            DisplayName = "Team One",
            SettingsConcurrencyToken = "settings-ct",
        }.Build();
        team.RootKeys = rootKeys.ToDictionary(
            pair => pair.Region,
            pair => pair.RootKey,
            StringComparer.Ordinal
        );
        return team;
    }

    private HttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
            RequestServices = new ServiceCollection()
                .AddSingleton(MockMediator.Object)
                .BuildServiceProvider(),
        };
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant
                    == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == ControlPlaneTenantId)
                )
            )
        );
        return httpContext;
    }

    private static string[] MapRoutes(TeamApiEndpointHandler handler)
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

    #region ListRootKeysAsync Tests

    [Fact]
    public async Task ListRootKeysAsync_WhenAuthorized_ReturnsTheKeysOrderedByRegion()
    {
        SetupAuthorization(Operations.Read);
        SetupStore(
            CreateTeam(
                (OwnRegion, new OwnerRootKeyRequest("aws-kms", "own-key")),
                (
                    OtherRegion,
                    new OwnerRootKeyRequest("aws-kms", "other-key") { AccessIdentity = "role" }
                )
            )
        );

        MockOwnerRootKeyManager
            .SetupGet(x => x.AllowedKeyWrapperNames)
            .Returns(["aws-kms", "gcp-kms"])
            .Verifiable();
        MockOwnerRootKeyManager
            .Setup(x => x.GetAccessBinding(TeamOwner))
            .Returns("binding-1")
            .Verifiable();

        var result = await Handler.ListRootKeysAsync(
            CreateHttpContext(),
            TeamId,
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<TeamRootKeysResource>>(result);
        var resource = Assert.IsType<TeamRootKeysResource>(ok.Value);
        var keys = resource.RootKeys;
        Assert.Equal([OtherRegion, OwnRegion], keys.Select(key => key.Region));
        Assert.Equal(["other-key", "own-key"], keys.Select(key => key.KeyReference));
        Assert.Equal("role", keys[0].AccessIdentity);
        Assert.Equal("aws-kms", keys[0].Provider);
        Assert.Equal(["aws-kms", "gcp-kms"], resource.RootKeyProviders);
        Assert.Equal("binding-1", resource.RootKeyAccessBinding);
    }

    [Fact]
    public async Task ListRootKeysAsync_WhenTheTeamIsUnknown_ReturnsNotFound()
    {
        SetupAuthorization(Operations.Read);
        SetupStore(null);

        var result = await Handler.ListRootKeysAsync(
            CreateHttpContext(),
            TeamId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task ListRootKeysAsync_WhenForbidden_ReturnsForbiddenWithoutReadingTheTeam()
    {
        SetupAuthorizationFailure(Operations.Read, _ => { });

        var result = await Handler.ListRootKeysAsync(
            CreateHttpContext(),
            TeamId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region SetRootKeyAsync Tests

    [Fact]
    public async Task SetRootKeyAsync_WhenForbidden_ReturnsForbiddenWithoutReadingTheTeam()
    {
        SetupAuthorizationFailure(Operations.Update, _ => { });

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task SetRootKeyAsync_WhenAuthorizing_AuthorizesAtTheTeamsNodeInTheControlPlaneTenant()
    {
        object? authorizedResource = null;
        SetupAuthorizationFailure(Operations.Update, resource => authorizedResource = resource);

        await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var node = Assert.IsType<ResourceNode>(authorizedResource);
        Assert.Equal(
            (ControlPlaneTenantId, ResourceNodeTypes.Team, TeamId),
            (node.TenantId, node.ResourceType, node.ResourceId)
        );
    }

    [Fact]
    public async Task SetRootKeyAsync_ForThisCellsRegion_AppliesTheKeyWritesAndPublishesIt()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam((OtherRegion, new OwnerRootKeyRequest("aws-kms", "other-key")));
        SetupStore(team);
        SetupCanBringRootKey();
        SetupValidRequest();
        SetupCells();
        SetupSetAsync();
        TeamDocument? published = null;
        SetupWritePublishAuditAndAnnounce(document => published = document);

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<TeamRootKeyResource>>(result);
        Assert.Equal(OwnRegion, ok.Value?.Region);
        Assert.Equal("aws-kms", ok.Value?.Provider);
        Assert.Equal(RootKeyReference, ok.Value?.KeyReference);
        Assert.Equal([OtherRegion, OwnRegion], team.RootKeys.Keys.Order(StringComparer.Ordinal));
        Assert.NotNull(published);
        Assert.Equal(RootKeyReference, published.RootKeys[OwnRegion].KeyReference);
        Assert.Equal("other-key", published.RootKeys[OtherRegion].KeyReference);
    }

    [Fact]
    public async Task SetRootKeyAsync_ForAnotherRegion_WritesAndPublishesWithoutApplyingTheKey()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam();
        SetupStore(team);
        SetupCanBringRootKey();
        SetupValidRequest();
        SetupCells();
        TeamDocument? published = null;
        SetupWritePublishAuditAndAnnounce(document => published = document);

        // The strict owner root key manager has no SetAsync setup: applying the key here fails the test.
        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OtherRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<TeamRootKeyResource>>(result);
        Assert.Equal(OtherRegion, ok.Value?.Region);
        Assert.Equal([OtherRegion], team.RootKeys.Keys);
        Assert.NotNull(published);
        Assert.Equal(RootKeyReference, published.RootKeys[OtherRegion].KeyReference);
    }

    [Fact]
    public async Task SetRootKeyAsync_WhenTheProbeFails_ReturnsUnprocessableAndWritesNothing()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam();
        SetupStore(team);
        SetupCanBringRootKey();
        SetupValidRequest();
        SetupCells();
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest("aws-kms", RootKeyReference),
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(
                new OwnerRootKeyProbeException(
                    TeamOwner,
                    "aws-kms",
                    RootKeyReference,
                    "AccessDenied"
                )
            )
            .Verifiable();

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Empty(team.RootKeys);
    }

    [Fact]
    public async Task SetRootKeyAsync_WithAnUnknownRegion_ReturnsAValidationProblemNamingTheRegion()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());
        SetupCanBringRootKey();
        SetupValidRequest();
        SetupCells();

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            "mars-1",
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains("region", problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task SetRootKeyAsync_WithAnInvalidReference_ReturnsAValidationProblemNamingIt()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());
        SetupCanBringRootKey();
        MockOwnerRootKeyManager
            .SetupGet(x => x.AllowedKeyWrapperNames)
            .Returns(["aws-kms"])
            .Verifiable();
        string? failure = "The key reference must be the ARN of a KMS key or alias.";
        MockOwnerRootKeyManager
            .Setup(x => x.TryValidateKeyReference("aws-kms", "nope", out failure))
            .Returns(false)
            .Verifiable();

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest("nope"),
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal([failure], problem.ProblemDetails.Errors["keyReference"]);
    }

    [Fact]
    public async Task SetRootKeyAsync_WhenAPolicyRefusesTheTeam_ReturnsBadRequestWithTheReason()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());
        string? reason = "A team cannot bring a root key.";
        MockOwnerRootKeyManager
            .Setup(x => x.CanBringRootKey(TeamOwner, out reason))
            .Returns(false)
            .Verifiable();

        var result = await Handler.SetRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(reason, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task SetRootKeyAsync_WhenTheDirectoryCannotListCells_ReturnsServiceUnavailable()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());
        SetupCanBringRootKey();
        SetupValidRequest();
        MockCellDirectory
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory is down."))
            .Verifiable();
        var httpContext = CreateHttpContext();

        var result = await Handler.SetRootKeyAsync(
            httpContext,
            TeamId,
            OwnRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(
            $"{GlobalDirectoryProtocol.UnavailableRetryAfterSeconds}",
            httpContext.Response.Headers.RetryAfter.ToString()
        );
    }

    [Fact]
    public async Task SetRootKeyAsync_WhenThePublishFails_ReturnsServiceUnavailableAfterTheWrite()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());
        SetupCanBringRootKey();
        SetupValidRequest();
        SetupCells();
        SetupWrite();
        SetupSave();
        SetupPublishUnavailable();
        SetupTeamChangedAudit();
        SetupTeamChangedAnnouncement();
        var httpContext = CreateHttpContext();

        var result = await Handler.SetRootKeyAsync(
            httpContext,
            TeamId,
            OtherRegion,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(
            $"{GlobalDirectoryProtocol.UnavailableRetryAfterSeconds}",
            httpContext.Response.Headers.RetryAfter.ToString()
        );
    }

    #endregion

    #region RemoveRootKeyAsync Tests

    [Fact]
    public async Task RemoveRootKeyAsync_WhenForbidden_ReturnsForbiddenWithoutReadingTheTeam()
    {
        SetupAuthorizationFailure(Operations.Update, _ => { });

        var result = await Handler.RemoveRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task RemoveRootKeyAsync_ForThisCellsRegion_RemovesTheKeyWritesAndPublishesIt()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam(
            (OwnRegion, new OwnerRootKeyRequest("aws-kms", RootKeyReference)),
            (OtherRegion, new OwnerRootKeyRequest("aws-kms", "other-key"))
        );
        SetupStore(team);
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<OwnerRootKeyInfo?>((OwnerRootKeyInfo?)null))
            .Verifiable();
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(
                new ValueTask<OwnerRootKeyRemoval>(
                    new OwnerRootKeyRemoval(null, false, new MasterKeyRewrapResult(0, 0, 0, 0))
                )
            )
            .Verifiable();
        TeamDocument? published = null;
        SetupWritePublishAuditAndAnnounce(document => published = document);

        var result = await Handler.RemoveRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal([OtherRegion], team.RootKeys.Keys);
        Assert.NotNull(published);
        Assert.Equal([OtherRegion], published.RootKeys.Keys);
    }

    [Fact]
    public async Task RemoveRootKeyAsync_WhenMasterKeysRemainUnderTheKey_ReturnsAcceptedWithTheRemovingKey()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam((OwnRegion, new OwnerRootKeyRequest("aws-kms", RootKeyReference)));
        SetupStore(team);
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<OwnerRootKeyInfo?>(CreateRootKeyInfo()))
            .Verifiable();
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(
                new ValueTask<OwnerRootKeyRemoval>(
                    new OwnerRootKeyRemoval(
                        CreateRootKeyInfo(OwnerRootKeyStates.Removing),
                        true,
                        new MasterKeyRewrapResult(2, 1, 0, 1)
                    )
                )
            )
            .Verifiable();
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordRootKeyChangedAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<OwnerRootKeyInfo>(),
                    RootKeyChanges.Removed,
                    RootKeyChangeReasons.Requested
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        SetupWritePublishAuditAndAnnounce(_ => { });

        var result = await Handler.RemoveRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CancellationToken.None
        );

        var accepted = Assert.IsType<Accepted<EncryptionRootKeyResource>>(result);
        Assert.Equal(EncryptionRootKeyStates.Removing, accepted.Value?.State);
        Assert.Empty(team.RootKeys);
    }

    [Fact]
    public async Task RemoveRootKeyAsync_ForAnotherRegion_WritesAndPublishesWithoutRemovingTheKeyHere()
    {
        SetupAuthorization(Operations.Update);
        var team = CreateTeam(
            (OwnRegion, new OwnerRootKeyRequest("aws-kms", RootKeyReference)),
            (OtherRegion, new OwnerRootKeyRequest("aws-kms", "other-key"))
        );
        SetupStore(team);
        TeamDocument? published = null;
        SetupWritePublishAuditAndAnnounce(document => published = document);

        // The strict owner root key manager has no setups: removing a key here fails the test.
        var result = await Handler.RemoveRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OtherRegion,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal([OwnRegion], team.RootKeys.Keys);
        Assert.NotNull(published);
        Assert.Equal([OwnRegion], published.RootKeys.Keys);
    }

    [Fact]
    public async Task RemoveRootKeyAsync_WithoutAnEntryForAnotherRegion_ReturnsNoContentAndWritesNothing()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam());

        var result = await Handler.RemoveRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OtherRegion,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
    }

    #endregion

    #region ValidateRootKeyAsync Tests

    [Fact]
    public async Task ValidateRootKeyAsync_WhenForbidden_ReturnsForbiddenWithoutReadingTheTeam()
    {
        SetupAuthorizationFailure(Operations.Update, _ => { });

        var result = await Handler.ValidateRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ValidateRootKeyAsync_ForThisCellsRegion_ReturnsTheKeyAsProbed()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam((OwnRegion, new OwnerRootKeyRequest("aws-kms", RootKeyReference))));
        MockOwnerRootKeyManager
            .Setup(x => x.ValidateAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(
                new ValueTask<OwnerRootKeyValidation>(
                    new OwnerRootKeyValidation(
                        CreateRootKeyInfo(),
                        null,
                        null,
                        new MasterKeyRewrapResult(0, 0, 0, 0)
                    )
                )
            )
            .Verifiable();

        var result = await Handler.ValidateRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OwnRegion,
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<EncryptionRootKeyResource>>(result);
        Assert.Equal(RootKeyReference, ok.Value?.KeyReference);
    }

    [Fact]
    public async Task ValidateRootKeyAsync_ForAnotherRegion_ReturnsConflict()
    {
        SetupAuthorization(Operations.Update);
        SetupStore(CreateTeam((OtherRegion, new OwnerRootKeyRequest("aws-kms", RootKeyReference))));

        var result = await Handler.ValidateRootKeyAsync(
            CreateHttpContext(),
            TeamId,
            OtherRegion,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/teams/{teamId}/encryption-keys/root-keys", new[] { 401, 403, 404 })]
    [InlineData(
        "PUT",
        "/teams/{teamId}/encryption-keys/root-keys/{region}",
        new[] { 400, 401, 403, 404, 409, 412, 415, 422, 503 }
    )]
    [InlineData(
        "DELETE",
        "/teams/{teamId}/encryption-keys/root-keys/{region}",
        new[] { 401, 403, 404, 412, 503 }
    )]
    [InlineData(
        "POST",
        "/teams/{teamId}/encryption-keys/root-keys/{region}/validate",
        new[] { 401, 403, 404, 409 }
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

    [Fact]
    public void Map_OnTheControlPlane_MapsTheRootKeyRoutes()
    {
        var routes = MapRoutes(CreateHandler(isControlPlane: true));

        Assert.Contains("/teams/{teamId}/encryption-keys/root-keys", routes);
        Assert.Contains("/teams/{teamId}/encryption-keys/root-keys/{region}", routes);
        Assert.Contains("/teams/{teamId}/encryption-keys/root-keys/{region}/validate", routes);
    }

    [Fact]
    public void Map_OnAnotherCell_MapsNoRootKeyRoute()
    {
        var routes = MapRoutes(CreateHandler(isControlPlane: false));

        Assert.DoesNotContain(
            routes,
            route => route.Contains("root-keys", StringComparison.Ordinal)
        );
    }

    #endregion
}
