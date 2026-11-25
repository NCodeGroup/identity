// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class CellPrincipalDirectoryTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IPlatformTopology> MockTopology { get; }

    private Mock<IControlPlanePrincipalDirectory> MockLocalDirectory { get; }

    private CellPrincipalDirectory Directory { get; }

    public CellPrincipalDirectoryTests()
    {
        MockTopology = MockRepository.Create<IPlatformTopology>();
        MockLocalDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        Directory = new CellPrincipalDirectory(MockTopology.Object, MockLocalDirectory.Object);
    }

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WithManagementTenant_ReturnsTheLocalAdapterAnswer()
    {
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["principal-1"] };
        var summary = new PrincipalSummaryResource
        {
            PrincipalId = "principal-1",
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = "Alice",
            UserName = "alice",
        };
        IReadOnlyList<PrincipalSummaryResource> expected = [summary];
        MockTopology.SetupGet(x => x.HasManagementTenant).Returns(true).Verifiable();
        MockLocalDirectory
            .Setup(x => x.FindAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .Verifiable();

        var result = await Directory.FindAsync(query, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task FindAsync_WithoutManagementTenant_ReturnsNoPrincipals()
    {
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["principal-1"] };
        MockTopology.SetupGet(x => x.HasManagementTenant).Returns(false).Verifiable();

        var result = await Directory.FindAsync(query, CancellationToken.None);

        Assert.Empty(result);
    }

    #endregion
}
