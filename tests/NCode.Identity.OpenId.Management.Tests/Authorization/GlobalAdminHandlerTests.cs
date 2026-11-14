// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management;

public class GlobalAdminHandlerTests : IDisposable
{
    private const string Subject = "subject-1";
    private const string ClientId = "client-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private sealed class TestRequirement : IAuthorizationRequirement;

    private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal CreateCaller() =>
        CreateUser(new Claim("sub", Subject), new Claim("client_id", ClientId));

    private Mock<IControlPlaneAuthority> SetupAuthority(
        Func<ControlPlaneAuthority> answer,
        Times times
    )
    {
        var authority = MockRepository.Create<IControlPlaneAuthority>();
        authority
            .Setup(x =>
                x.GetAsync(
                    It.Is<ControlPlaneAuthorityRequest>(request =>
                        request.Subject == Subject
                        && request.ClientId == ClientId
                        && request.TeamId == null
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() => ValueTask.FromResult(answer()))
            .Verifiable(times);
        return authority;
    }

    private static async ValueTask<bool> HandleAsync(
        IControlPlaneAuthority authority,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var context = new AuthorizationHandlerContext(
            [new TestRequirement()],
            user,
            resource: null
        );

        await new GlobalAdminHandler(authority).HandleAsync(context);

        return context.HasSucceeded;
    }

    #region HandleRequirementAsync Tests

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneSaysGlobalAdmin_Succeeds()
    {
        var authority = SetupAuthority(
            () =>
                new ControlPlaneAuthority
                {
                    PrincipalId = "principal-1",
                    PlatformRoles = [BuiltInRoles.GlobalAdmin],
                    IsGlobalAdmin = true,
                },
            Times.Once()
        );

        var succeeded = await HandleAsync(authority.Object, CreateCaller(), CancellationToken.None);

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneSaysNotGlobalAdmin_DoesNotSucceed()
    {
        var authority = SetupAuthority(
            () =>
                new ControlPlaneAuthority
                {
                    PrincipalId = "principal-1",
                    PlatformRoles = [BuiltInRoles.TeamAdmin],
                },
            Times.Once()
        );

        var succeeded = await HandleAsync(authority.Object, CreateCaller(), CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneDoesNotKnowTheCaller_DoesNotSucceed()
    {
        var authority = SetupAuthority(() => ControlPlaneAuthority.None, Times.Once());

        var succeeded = await HandleAsync(authority.Object, CreateCaller(), CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheUserHoldsOnlyARoleClaim_DoesNotSucceed()
    {
        var authority = SetupAuthority(() => ControlPlaneAuthority.None, Times.Once());
        var user = CreateUser(
            new Claim("sub", Subject),
            new Claim("client_id", ClientId),
            new Claim(ClaimTypes.Role, BuiltInRoles.GlobalAdmin)
        );

        var succeeded = await HandleAsync(authority.Object, user, CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheSubjectClaimIsMissing_DoesNotAskTheControlPlane()
    {
        var authority = MockRepository.Create<IControlPlaneAuthority>();

        var succeeded = await HandleAsync(
            authority.Object,
            CreateUser(new Claim("client_id", ClientId)),
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheClientIdClaimIsMissing_DoesNotAskTheControlPlane()
    {
        var authority = MockRepository.Create<IControlPlaneAuthority>();

        var succeeded = await HandleAsync(
            authority.Object,
            CreateUser(new Claim("sub", Subject)),
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneIsUnavailable_Throws()
    {
        var authority = SetupAuthority(
            () => throw new DirectoryUnavailableException("unavailable"),
            Times.Once()
        );

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await HandleAsync(authority.Object, CreateCaller(), CancellationToken.None)
        );
    }

    #endregion
}
