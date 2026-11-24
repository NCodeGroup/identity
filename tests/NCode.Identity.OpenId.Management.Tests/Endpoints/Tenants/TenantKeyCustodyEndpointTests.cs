// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Management.Endpoints.Secrets;
using NCode.Identity.OpenId.Management.Promotions;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Settings;
using NCode.Mediator;
using SystemTextJsonPatch;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

public sealed class TenantKeyCustodyEndpointTests
{
    private const string TenantId = "tenant-1";
    private const string CellName = "cell-1";
    private const string ControlPlaneTenantId = "platform-root";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; } = new();
    private Mock<IStoreManager> MockStoreManager { get; } = new();
    private Mock<ITenantStore> MockTenantStore { get; } = new();
    private Mock<ITenantValidator> MockTenantValidator { get; } = new();
    private Mock<ITenantResolver> MockTenantResolver { get; } = new();
    private Mock<IAuthorizationService> MockAuthorizationService { get; } = new();
    private Mock<ITenantDirectory> MockTenantDirectory { get; } = new(MockBehavior.Strict);
    private Mock<IRemoteSecretDestroyer> MockRemoteSecretDestroyer { get; } = new();
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; } = new();
    private Mock<IMediator> MockMediator { get; } = new();
    private TenantApiEndpointHandler Handler { get; }

    public TenantKeyCustodyEndpointTests()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(AuthorizationResult.Success());
        MockTenantValidator
            .Setup(x =>
                x.AuthorizeUpdateAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    TenantId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null);
        MockTenantValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.IsAny<PersistedTenant>(),
                    It.IsAny<UpdateTenantRequest>(),
                    It.IsAny<string?>(),
                    It.IsAny<IStoreManager>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null);

        Handler = new TenantApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockTenantValidator.Object,
            MockTenantResolver.Object,
            Mock.Of<ISecretValidator>(),
            Mock.Of<ISecretGenerator>(),
            Mock.Of<IClientKeyImporter>(),
            Mock.Of<ISecretPublicKeyConverter>(),
            Mock.Of<ISecretProtectorProvider>(),
            Mock.Of<IMasterKeyManager>(),
            Mock.Of<IOwnerRootKeyManager>(),
            TimeProvider.System,
            Mock.Of<ICryptoService>(),
            Mock.Of<ISettingSerializer>(),
            Mock.Of<ISettingScopeCollectionProvider>(),
            Mock.Of<IEffectiveSettingsResolver>(),
            Mock.Of<ITenantEnvironmentCatalog>(),
            Mock.Of<ITenantClientCoherenceChecker>(),
            Mock.Of<IResourceOwnershipService>(),
            Mock.Of<IControlPlanePrincipalDirectory>(),
            MockRemoteSecretDestroyer.Object,
            MockTenantDirectory.Object,
            Options.Create(
                new CellOptions
                {
                    Name = CellName,
                    Region = "us",
                    Kind = CellKinds.Shared,
                }
            ),
            Mock.Of<ITenantPlacer>(),
            Mock.Of<ITenantConfigurationExporter>(),
            MockManagementAuditRecorder.Object,
            NullLogger<TenantApiEndpointHandler>.Instance
        );
    }

    [Theory]
    [InlineData(TenantKeyCustody.Outgoing)]
    [InlineData(TenantKeyCustody.Incoming)]
    public async Task DeleteSecretAsync_WhenTheTenantIsFenced_ReturnsConflictWithoutReadingTheDirectory(
        string custody
    )
    {
        SetupTenant(custody, "cell-2");
        MockTenantStore
            .Setup(x =>
                x.GetSecretOrDefaultAsync(TenantId, "secret-1", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateSecret());

        var result = await Handler.DeleteSecretAsync(
            CreateHttpContext(),
            TenantId,
            "secret-1",
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        MockTenantStore.Verify(
            x => x.RemoveSecretAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never
        );
        MockTenantDirectory.VerifyNoOtherCalls();
        MockRemoteSecretDestroyer.Verify(
            x => x.DestroyAsync(It.IsAny<IReadOnlyCollection<PersistedSecret>>()),
            Times.Never
        );
    }

    [Fact]
    public async Task DeleteSecretAsync_WhenTheTenantIsHeld_DeletesWithoutReadingTheDirectory()
    {
        SetupTenant(TenantKeyCustody.Held, null);
        MockTenantStore
            .Setup(x =>
                x.GetSecretOrDefaultAsync(TenantId, "secret-1", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateSecret());

        var result = await Handler.DeleteSecretAsync(
            CreateHttpContext(),
            TenantId,
            "secret-1",
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        MockTenantDirectory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenEnablingAFencedTenantPlacedHere_SetsHeldInTheSameSave()
    {
        var tenant = SetupTenant(TenantKeyCustody.Incoming, "cell-2");
        SetupPlacement(CellName);
        var saved = false;
        MockTenantStore
            .Setup(x => x.UpdateAsync(tenant, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.Equal(TenantKeyCustody.Held, tenant.KeyCustody);
                saved = true;
            })
            .Returns(ValueTask.CompletedTask);

        var result = await Handler.UpdateTenantAsync(
            CreateHttpContext(),
            TenantId,
            EnablePatch(),
            ifMatch: null,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.True(saved);
        Assert.False(tenant.IsDisabled);
        Assert.Equal(TenantKeyCustody.Held, tenant.KeyCustody);
        Assert.Null(tenant.KeyCustodyCell);
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenEnablingAFencedTenantPlacedElsewhere_ReturnsConflict()
    {
        var tenant = SetupTenant(TenantKeyCustody.Outgoing, "cell-2");
        SetupPlacement("cell-2");

        var result = await Handler.UpdateTenantAsync(
            CreateHttpContext(),
            TenantId,
            EnablePatch(),
            ifMatch: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(TenantKeyCustody.Outgoing, tenant.KeyCustody);
        MockTenantStore.Verify(
            x => x.UpdateAsync(It.IsAny<PersistedTenant>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenEnablingAFencedTenantTheDirectoryPlacesNowhere_ReturnsConflict()
    {
        SetupTenant(TenantKeyCustody.Incoming, "cell-2");
        SetupPlacement(null);

        var result = await Handler.UpdateTenantAsync(
            CreateHttpContext(),
            TenantId,
            EnablePatch(),
            ifMatch: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenEnablingAFencedTenantAndTheDirectoryIsUnavailable_ReturnsServiceUnavailable()
    {
        var tenant = SetupTenant(TenantKeyCustody.Incoming, "cell-2");
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory is unreachable."));
        var httpContext = CreateHttpContext();

        var result = await Handler.UpdateTenantAsync(
            httpContext,
            TenantId,
            EnablePatch(),
            ifMatch: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(TenantKeyCustody.Incoming, tenant.KeyCustody);
        MockTenantStore.Verify(
            x => x.UpdateAsync(It.IsAny<PersistedTenant>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenEnablingAHeldTenant_DoesNotReadTheDirectory()
    {
        SetupTenant(TenantKeyCustody.Held, null);
        MockTenantStore
            .Setup(x => x.UpdateAsync(It.IsAny<PersistedTenant>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var result = await Handler.UpdateTenantAsync(
            CreateHttpContext(),
            TenantId,
            EnablePatch(),
            ifMatch: null,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        MockTenantDirectory.VerifyNoOtherCalls();
    }

    private static JsonPatchDocument<UpdateTenantRequest> EnablePatch()
    {
        var patch = new JsonPatchDocument<UpdateTenantRequest>();
        patch.Replace(x => x.IsDisabled, false);
        return patch;
    }

    private PersistedTenant SetupTenant(string custody, string? cell)
    {
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            ConcurrencyToken = "tenant-ct",
            Issuer = "https://op.example/tenant-1",
            MtlsEndpointAliasAuthority = "mtls.op.example",
            IsDisabled = true,
        }.Build();
        tenant.KeyCustody = custody;
        tenant.KeyCustodyCell = cell;
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        return tenant;
    }

    private void SetupPlacement(string? cell) =>
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                cell is null
                    ? null
                    : new TenantPlacement
                    {
                        TenantId = TenantId,
                        Issuer = "https://op.example/tenant-1",
                        Cell = cell,
                        Region = "us",
                    }
            );

    private static PersistedSecret CreateSecret() =>
        new PersistedSecretBuilder
        {
            SecretId = "secret-1",
            ConcurrencyToken = "secret-ct",
            Use = "sig",
            Algorithm = "RS256",
            EncodedValue = "encoded",
        }.Build();

    private HttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
            RequestServices = new ServiceCollection()
                .AddSingleton(Mock.Of<ITenantProvisioner>())
                .AddSingleton(MockMediator.Object)
                .BuildServiceProvider(),
        };
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
}
