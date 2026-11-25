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
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Platform;
using NCode.Identity.OpenId.Management.Endpoints.Teams;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Endpoints.Platform;

public sealed class ManagementKeyRevocationTests : IDisposable
{
    private const string KeyId = "kid-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<IManagementKeyRevoker> MockManagementKeyRevoker { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private PlatformApiEndpointHandler Handler { get; }

    public ManagementKeyRevocationTests()
    {
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockManagementKeyRevoker = MockRepository.Create<IManagementKeyRevoker>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();

        Handler = new PlatformApiEndpointHandler(
            MockRepository.Create<IStoreManagerFactory>().Object,
            MockAuthorizationService.Object,
            MockRepository.Create<ICryptoService>().Object,
            MockRepository.Create<ISettingSerializer>().Object,
            MockRepository.Create<ISettingScopeCollectionProvider>().Object,
            MockRepository.Create<IEffectiveSettingsResolver>().Object,
            MockRepository.Create<IClientCoherenceValidator>().Object,
            MockRepository.Create<ITeamClientCoherenceChecker>().Object,
            MockRepository.Create<IMasterKeyManager>().Object,
            MockManagementAuditRecorder.Object,
            MockRepository.Create<IPlatformDirectory>().Object,
            NullLogger<PlatformApiEndpointHandler>.Instance,
            Options.Create(new CellOptions { IsControlPlane = true })
        );
    }

    public void Dispose() => MockRepository.Verify();

    private void SetupAuthorization(AuthorizationResult result) =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();

    private DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(MockManagementKeyRevoker.Object)
                .BuildServiceProvider(),
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
                        == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == "control-plane")
                )
            )
        );
        return httpContext;
    }

    [Fact]
    public async Task RevokeManagementKeyAsync_WhenNotAuthorized_ReturnsForbiddenWithoutRevoking()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.RevokeManagementKeyAsync(
            CreateHttpContext(),
            KeyId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task RevokeManagementKeyAsync_WhenTheKeyIsUnknown_ReturnsNotFoundAndRecordsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        MockManagementKeyRevoker
            .Setup(x => x.RevokeAsync(KeyId, It.IsAny<CancellationToken>()))
            .Returns(
                new ValueTask<ManagementKeyRevocationResult>(ManagementKeyRevocationResult.NotFound)
            )
            .Verifiable();

        var result = await Handler.RevokeManagementKeyAsync(
            CreateHttpContext(),
            KeyId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task RevokeManagementKeyAsync_WhenRevoked_ReturnsTheRevocationAndRecordsOneAuditEvent()
    {
        SetupAuthorization(AuthorizationResult.Success());
        MockManagementKeyRevoker
            .Setup(x => x.RevokeAsync(KeyId, It.IsAny<CancellationToken>()))
            .Returns(
                new ValueTask<ManagementKeyRevocationResult>(
                    ManagementKeyRevocationResult.Revoked("kid-2")
                )
            )
            .Verifiable();
        JsonElement? recorded = null;
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordPlatformChangedAsync(
                    It.IsAny<HttpContext>(),
                    ResourceChangeTypes.KeyRevoked,
                    It.IsAny<JsonElement?>()
                )
            )
            .Callback((HttpContext _, string _, JsonElement? values) => recorded = values)
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        var result = await Handler.RevokeManagementKeyAsync(
            CreateHttpContext(),
            KeyId,
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<ManagementKeyRevocationResource>>(result);
        Assert.Equal(KeyId, ok.Value?.RevokedKeyId);
        Assert.Equal("kid-2", ok.Value?.ReplacementKeyId);
        Assert.Equal(KeyId, recorded?.GetProperty("revokedKeyId").GetString());
        Assert.Equal("kid-2", recorded?.GetProperty("replacementKeyId").GetString());
    }
}
