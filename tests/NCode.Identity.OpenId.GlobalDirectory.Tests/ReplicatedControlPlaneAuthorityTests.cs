// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.OpenId.Management.ControlPlane;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class ReplicatedControlPlaneAuthorityTests : BaseTests
{
    private const string Subject = "principal-1";
    private const string ClientId = "client-1";

    private Mock<IControlPlaneReplica> ReplicaMock { get; }

    public ReplicatedControlPlaneAuthorityTests()
    {
        ReplicaMock = CreateStrictMock<IControlPlaneReplica>();
    }

    private ReplicatedControlPlaneAuthority CreateAuthority() => new(ReplicaMock.Object);

    private static ControlPlaneAuthorityRequest CreateRequest(string? teamId = null) =>
        new()
        {
            Subject = Subject,
            ClientId = ClientId,
            TeamId = teamId,
        };

    private void SetupReplica(string? teamId, ReplicatedAuthority authority) =>
        ReplicaMock
            .Setup(x => x.GetAuthorityAsync(Subject, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authority);

    [Fact]
    public async Task GetAsync_PlatformGlobalAdmin_IsGlobalAdmin()
    {
        SetupReplica(null, new ReplicatedAuthority { PlatformRoles = ["GlobalAdmin"] });

        var result = await CreateAuthority().GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(Subject, result.PrincipalId);
        Assert.True(result.IsGlobalAdmin);
        Assert.False(result.IsActingClient);
        Assert.Equal(["GlobalAdmin"], result.PlatformRoles);
        Assert.Empty(result.TeamRoles);
    }

    [Fact]
    public async Task GetAsync_TeamChainRoles_ReturnsTeamRoles()
    {
        SetupReplica("team-a", new ReplicatedAuthority { TeamRoles = ["TeamAdmin", "TeamViewer"] });

        var result = await CreateAuthority()
            .GetAsync(CreateRequest("team-a"), CancellationToken.None);

        Assert.False(result.IsGlobalAdmin);
        Assert.Equal(["TeamAdmin", "TeamViewer"], result.TeamRoles);
    }

    [Fact]
    public async Task GetAsync_ActingClient_IsActingClient()
    {
        SetupReplica(null, new ReplicatedAuthority { ClientId = ClientId });

        var result = await CreateAuthority().GetAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsActingClient);
        Assert.Equal(Subject, result.PrincipalId);
    }

    [Fact]
    public async Task GetAsync_OtherClient_IsNotActingClient()
    {
        SetupReplica(null, new ReplicatedAuthority { ClientId = "other-client" });

        var result = await CreateAuthority().GetAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.IsActingClient);
    }

    [Fact]
    public async Task GetAsync_UnknownSubject_ReturnsNone()
    {
        SetupReplica(null, new ReplicatedAuthority());

        var result = await CreateAuthority().GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(ControlPlaneAuthority.None, result);
    }
}
