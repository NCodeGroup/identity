// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;

namespace NCode.Identity.OpenId.Management.Endpoints;

internal static class PrincipalDirectoryMocks
{
    public static PrincipalSummaryResource CreateSummary(
        string principalId,
        string principalType,
        string displayName,
        string? userName = null
    ) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = principalType,
            DisplayName = displayName,
            UserName = userName,
        };

    private static ValueTask<IReadOnlyList<PrincipalSummaryResource>> DescribeAsClients(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var summaries = new List<PrincipalSummaryResource>(query.PrincipalIds.Count);
        foreach (var id in query.PrincipalIds)
        {
            var summary = CreateSummary(id, PrincipalTypes.Client, id);
            summaries.Add(summary);
        }

        return ValueTask.FromResult<IReadOnlyList<PrincipalSummaryResource>>(summaries);
    }

    public static void SetupDescribingEveryPrincipal(Mock<IControlPlanePrincipalDirectory> mock) =>
        mock.Setup(x =>
                x.FindAsync(It.IsAny<ControlPlanePrincipalQuery>(), It.IsAny<CancellationToken>())
            )
            .Returns(
                (ControlPlanePrincipalQuery query, CancellationToken cancellationToken) =>
                    DescribeAsClients(query, cancellationToken)
            )
            .Verifiable();
}
