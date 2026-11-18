// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Secrets.Persistence.Protection;

public class DefaultSecretOwnerParentResolverTests
{
    private DefaultSecretOwnerParentResolver Resolver { get; } = new();

    [Theory]
    [InlineData("platform")]
    [InlineData("Platform")]
    public async Task GetAncestorsAsync_ThePlatform_HasNone(string ownerType)
    {
        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(ownerType, SecretOwner.PlatformOwnerId),
            CancellationToken.None
        );

        Assert.Empty(ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_ATenant_IsThePlatformOnly()
    {
        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("tenant-a"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_ATeam_IsThePlatformOnly()
    {
        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(SecretOwnerTypes.Team, "team-a"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }
}
