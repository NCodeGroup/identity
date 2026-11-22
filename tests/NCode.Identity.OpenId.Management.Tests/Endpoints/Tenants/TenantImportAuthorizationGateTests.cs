// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

// See ADR-1520.
public class TenantImportAuthorizationGateTests : IDisposable
{
    private const string TenantId = "tenant-1";

    private MockRepository MockRepository { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private TenantImportAuthorizationGate Gate { get; }

    public TenantImportAuthorizationGateTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();

        Gate = new TenantImportAuthorizationGate(MockAuthorizationService.Object);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private static DefaultHttpContext CreateHttpContext()
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        // The management caller gate publishes the context in the pipeline; emulate it for direct gate calls.
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == TenantId)
                )
            )
        );

        return httpContext;
    }

    private static bool IsPlatformNode(object? resource) =>
        resource
            is ResourceNode
            {
                TenantId: TenantId,
                ResourceType: ResourceNodeTypes.Platform,
                ResourceId: OpenIdConstants.PlatformResourceId,
            };

    private void SetupAuthorization(HttpContext httpContext, AuthorizationResult result)
    {
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    httpContext.User,
                    It.Is<object?>(resource => IsPlatformNode(resource)),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                        requirements.SequenceEqual(new[] { Operations.Update })
                    )
                )
            )
            .ReturnsAsync(result)
            .Verifiable();
    }

    #endregion

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenTheCallerIsAuthorized_ReturnsNull()
    {
        var httpContext = CreateHttpContext();
        SetupAuthorization(httpContext, AuthorizationResult.Success());

        var result = await Gate.InvokeAsync(httpContext, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheCallerIsNotAuthorized_AnswersForbidden()
    {
        var httpContext = CreateHttpContext();
        SetupAuthorization(httpContext, AuthorizationResult.Failed());

        var result = await Gate.InvokeAsync(httpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(
            "The caller is not authorized for this operation.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task InvokeAsync_WhenTheControlPlaneIsUnavailable_Answers503WithRetryAfter()
    {
        var httpContext = CreateHttpContext();
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ThrowsAsync(new DirectoryUnavailableException("The control plane is unavailable."))
            .Verifiable();

        var result = await Gate.InvokeAsync(httpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(
            $"{GlobalDirectoryProtocol.UnavailableRetryAfterSeconds}",
            httpContext.Response.Headers.RetryAfter.ToString()
        );
    }

    #endregion
}
