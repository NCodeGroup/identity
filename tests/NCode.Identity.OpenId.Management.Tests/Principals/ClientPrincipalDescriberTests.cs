// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Principals;

public sealed class ClientPrincipalDescriberTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IClientStore> MockClientStore { get; }

    private ClientPrincipalDescriber Describer { get; }

    public ClientPrincipalDescriberTests()
    {
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockClientStore = MockRepository.Create<IClientStore>();

        Describer = new ClientPrincipalDescriber(MockAmbientTenantAccessor.Object);
    }

    private void SetupClients(string[] principalIds, IReadOnlyList<PersistedClient> clients)
    {
        MockStoreManager
            .Setup(x => x.GetStore<IClientStore>())
            .Returns(MockClientStore.Object)
            .Verifiable();
        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("principal description"))
            .Returns(scope.Object)
            .Verifiable();
        MockClientStore
            .Setup(x =>
                x.GetByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(clients)
            .Verifiable();
    }

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenAClientHoldsThePrincipal_DescribesItByClientId()
    {
        var client = new PersistedClientBuilder
        {
            TenantId = "control-plane",
            ClientId = "cell-eu",
            PrincipalId = "p1",
        }.Build();
        SetupClients(["p1", "p2"], [client]);

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p2"], UserNames = ["alice"] },
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.Client, summary.PrincipalType);
        Assert.Equal("cell-eu", summary.DisplayName);
        Assert.Null(summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WhenNoClientHoldsThePrincipals_ReturnsNothing()
    {
        SetupClients(["p2"], []);

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesNoPrincipal_ReadsNoStore()
    {
        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { UserNames = ["alice"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    #endregion
}
