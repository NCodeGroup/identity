// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.Secrets.Persistence.Protection;

public class DefaultNearestOwnerRootKeyResolverTests : BaseTests
{
    private static DateTimeOffset Now { get; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static SecretOwner Tenant { get; } = SecretOwner.ForTenant("tenant-a");
    private static SecretOwner Team { get; } = new(SecretOwnerTypes.Team, "team-a");

    private Mock<ISecretOwnerParentResolver> Parents { get; }
    private Mock<IOwnerRootKeyStore> Store { get; }

    public DefaultNearestOwnerRootKeyResolverTests()
    {
        Parents = CreateStrictMock<ISecretOwnerParentResolver>();
        Store = CreateStrictMock<IOwnerRootKeyStore>();
    }

    private DefaultNearestOwnerRootKeyResolver CreateResolver() => new(Parents.Object);

    private void SetupAncestors(params SecretOwner[] ancestors) =>
        Parents
            .Setup(x => x.GetAncestorsAsync(Tenant, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<SecretOwner>>(ancestors))
            .Verifiable(Times.AtMost(int.MaxValue));

    private void SetupFind(SecretOwner owner, PersistedOwnerRootKey? row) =>
        Store
            .Setup(x => x.FindAsync(owner, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PersistedOwnerRootKey?>(row))
            .Verifiable(Times.AtMost(int.MaxValue));

    private static PersistedOwnerRootKey CreateRootKey(
        SecretOwner owner,
        string state = OwnerRootKeyStates.Active
    ) =>
        new()
        {
            OwnerType = owner.OwnerType,
            OwnerId = owner.OwnerId,
            KeyWrapperName = "fake-kms",
            KeyReference = $"kms://{owner.OwnerId}/root",
            State = state,
            CreatedWhen = Now,
            StateChangedWhen = Now,
        };

    #region FindNearestAsync Tests

    [Fact]
    public async Task FindNearest_TheOwnerBringsAKey_IsTheOwnersKey()
    {
        var own = CreateRootKey(Tenant);
        SetupFind(Tenant, own);

        var nearest = await CreateResolver()
            .FindNearestAsync(Store.Object, Tenant, CancellationToken.None);

        Assert.Same(own, nearest);
    }

    [Fact]
    public async Task FindNearest_TheOwnerIdDiffersInCase_FindsTheKeyOfTheNormalizedOwner()
    {
        var own = CreateRootKey(Tenant);
        SetupFind(Tenant, own);

        var nearest = await CreateResolver()
            .FindNearestAsync(
                Store.Object,
                SecretOwner.ForTenant("Tenant-A"),
                CancellationToken.None
            );

        Assert.Same(own, nearest);
    }

    [Fact]
    public async Task FindNearest_TheOwnerBringsNoKey_IsTheKeyOfTheNearestAncestorThatBringsOne()
    {
        var teamKey = CreateRootKey(Team);
        SetupFind(Tenant, null);
        SetupFind(Team, teamKey);
        SetupAncestors(Team, SecretOwner.Platform);

        var nearest = await CreateResolver()
            .FindNearestAsync(Store.Object, Tenant, CancellationToken.None);

        Assert.Same(teamKey, nearest);
    }

    [Fact]
    public async Task FindNearest_TheOwnersKeyIsRemoving_IsTheKeyOfTheNearestAncestor()
    {
        var teamKey = CreateRootKey(Team);
        SetupFind(Tenant, CreateRootKey(Tenant, OwnerRootKeyStates.Removing));
        SetupFind(Team, teamKey);
        SetupAncestors(Team, SecretOwner.Platform);

        var nearest = await CreateResolver()
            .FindNearestAsync(Store.Object, Tenant, CancellationToken.None);

        Assert.Same(teamKey, nearest);
    }

    [Fact]
    public async Task FindNearest_NoAncestorBringsAKey_IsNothing()
    {
        SetupFind(Tenant, null);
        SetupFind(Team, CreateRootKey(Team, OwnerRootKeyStates.Removing));
        SetupFind(SecretOwner.Platform, null);
        SetupAncestors(Team, SecretOwner.Platform);

        var nearest = await CreateResolver()
            .FindNearestAsync(Store.Object, Tenant, CancellationToken.None);

        Assert.Null(nearest);
    }

    #endregion
}
