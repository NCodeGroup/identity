// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

public sealed class DedicatedControlPlaneCellTests
{
    private const string ControlPlaneTenantId = "platform-root";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; } = new(MockBehavior.Strict);
    private Mock<ITenantValidator> MockTenantValidator { get; } = new();

    private TenantApiEndpointHandler CreateHandler(IPlatformTopology topology) =>
        new(
            MockStoreManagerFactory.Object,
            Mock.Of<IAuthorizationService>(),
            MockTenantValidator.Object,
            Mock.Of<ITenantResolver>(),
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
            Mock.Of<IRemoteSecretDestroyer>(),
            Mock.Of<ITenantDirectory>(),
            Options.Create(
                new CellOptions
                {
                    Name = "cell-1",
                    Region = "us",
                    Kind = CellKinds.Shared,
                    IsControlPlane = true,
                }
            ),
            topology,
            Mock.Of<ITenantPlacer>(),
            Mock.Of<ITenantConfigurationExporter>(),
            Mock.Of<IManagementAuditRecorder>(),
            NullLogger<TenantApiEndpointHandler>.Instance
        );

    private static HttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
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

    [Fact]
    public async Task CreateTenantAsync_WhenTheCellIsTheControlPlaneCellOfAPlatformWithADirectory_ReturnsConflict()
    {
        var topology = Mock.Of<IPlatformTopology>(x =>
            x.HasDirectory == true && x.IsControlPlaneCell == true
        );
        MockTenantValidator
            .Setup(x =>
                x.AuthorizeCreateAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    ControlPlaneTenantId,
                    "team-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null);

        var result = await CreateHandler(topology)
            .CreateTenantAsync(
                CreateHttpContext(),
                "team-1",
                new CreateTenantRequest
                {
                    TenantId = "acme",
                    DisplayName = "Acme",
                    IsDisabled = false,
                    Settings = JsonElement.Parse("{}"),
                },
                CancellationToken.None
            );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains(
            "holds only the control-plane tenant",
            problem.ProblemDetails.Detail,
            StringComparison.Ordinal
        );
        MockStoreManagerFactory.VerifyNoOtherCalls();
    }
}
