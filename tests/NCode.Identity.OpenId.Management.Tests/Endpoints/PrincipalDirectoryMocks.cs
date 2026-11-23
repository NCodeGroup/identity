// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;

namespace NCode.Identity.OpenId.Management.Endpoints;

internal static class PrincipalDirectoryMocks
{
    public static Mock<IControlPlanePrincipalDirectory> CreateDescribingEveryPrincipal()
    {
        var mock = new Mock<IControlPlanePrincipalDirectory>();
        mock.Setup(x =>
                x.FindAsync(It.IsAny<ControlPlanePrincipalQuery>(), It.IsAny<CancellationToken>())
            )
            .Returns(
                (ControlPlanePrincipalQuery query, CancellationToken _) =>
                    ValueTask.FromResult<IReadOnlyList<PrincipalSummaryResource>>(
                        query
                            .PrincipalIds.Select(id => new PrincipalSummaryResource
                            {
                                PrincipalId = id,
                                PrincipalType = PrincipalTypes.Client,
                                DisplayName = id,
                            })
                            .ToList()
                    )
            );
        return mock;
    }
}
