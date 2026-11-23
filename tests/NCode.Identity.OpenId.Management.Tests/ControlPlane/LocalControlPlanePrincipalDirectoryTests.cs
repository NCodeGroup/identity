// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class LocalControlPlanePrincipalDirectoryTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private const string RootTenantId = "control-plane";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ITenantResolver> MockTenantResolver { get; }
    private Mock<IStoreManager> MockStoreManager { get; }

    public LocalControlPlanePrincipalDirectoryTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockTenantResolver = MockRepository.Create<ITenantResolver>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
    }

    private LocalControlPlanePrincipalDirectory CreateDirectory(
        params IPrincipalDescriber[] describers
    ) =>
        new(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockTenantResolver.Object,
            describers
        );

    private void SetupScope()
    {
        MockTenantResolver
            .Setup(x => x.GetControlPlaneTenantId())
            .Returns(RootTenantId)
            .Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(RootTenantId))
            .Returns(scope.Object)
            .Verifiable();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private static PrincipalSummaryResource CreateSummary(string principalId, string displayName) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = displayName,
            UserName = null,
        };

    private Mock<IPrincipalDescriber> CreateDescriber(
        ControlPlanePrincipalQuery query,
        params PrincipalSummaryResource[] summaries
    )
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        describer
            .Setup(x => x.FindAsync(MockStoreManager.Object, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries)
            .Verifiable();
        return describer;
    }

    [Fact]
    public async Task FindAsync_WithAnEmptyQuery_SkipsTheStore()
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery(),
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_MergesTheDescribersAndTheFirstWins()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p2"] };
        var first = CreateDescriber(query, CreateSummary("p1", "First"));
        var second = CreateDescriber(
            query,
            CreateSummary("p1", "Second"),
            CreateSummary("p2", "Other")
        );
        var directory = CreateDirectory(first.Object, second.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Collection(
            result,
            summary =>
            {
                Assert.Equal("p1", summary.PrincipalId);
                Assert.Equal("First", summary.DisplayName);
            },
            summary =>
            {
                Assert.Equal("p2", summary.PrincipalId);
                Assert.Equal("Other", summary.DisplayName);
            }
        );
    }

    [Fact]
    public async Task FindAsync_WithoutDescribers_ReturnsNothing()
    {
        SetupScope();
        var directory = CreateDirectory();

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery { UserNames = ["alice"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }
}
