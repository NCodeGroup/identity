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

        MockStoreManager
            .Setup(x => x.GetStore<IClientStore>())
            .Returns(MockClientStore.Object)
            .Verifiable();
    }

    private void SetupClient(string principalId, PersistedClient? client)
    {
        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("principal description"))
            .Returns(scope.Object)
            .Verifiable();
        MockClientStore
            .Setup(x => x.GetByPrincipalOrDefaultAsync(principalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client)
            .Verifiable();
    }

    [Fact]
    public async Task FindAsync_WithAClientPrincipal_DescribesItByClientId()
    {
        var client = new PersistedClientBuilder
        {
            TenantId = "control-plane",
            ClientId = "cell-eu",
            PrincipalId = "p1",
        }.Build();
        SetupClient("p1", client);

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p1"], UserNames = ["alice"] },
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.Client, summary.PrincipalType);
        Assert.Equal("cell-eu", summary.DisplayName);
        Assert.Null(summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WithAnUnknownPrincipal_SkipsIt()
    {
        SetupClient("p2", null);

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }
}
