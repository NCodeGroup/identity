// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Provides the <see cref="IControlPlanePrincipalDirectory"/> of a cell that is not the control plane. It posts the
/// names to the control-plane cell's <c>control-plane/v1/principals</c> endpoint, at the origin of
/// <c>Cell:ControlPlane:Issuer</c>, with a client-credentials token for the audience
/// <see cref="GlobalDirectoryProtocol.ControlPlaneAudience"/>, in queries of at most
/// <see cref="ControlPlanePrincipalQuery.MaxNames"/> names each. It fails closed: a missing issuer, a failed call or an
/// unreadable answer is a <see cref="DirectoryUnavailableException"/>. It keeps no cache.
/// </summary>
// See ADR-1411.
internal sealed class RemoteControlPlanePrincipalDirectory(
    DirectoryHttpClient client,
    IOptions<CellOptions> cellOptions
) : IControlPlanePrincipalDirectory
{
    private DirectoryHttpClient Client { get; } = client;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PrincipalIds.Count == 0 && query.UserNames.Count == 0)
            return [];

        var principalsUri = ControlPlaneUris.Create(
            CellOptions,
            GlobalDirectoryProtocol.ControlPlanePrincipalsPath
        );
        var capacity = query.PrincipalIds.Count + query.UserNames.Count;
        var names = new List<(bool IsId, string Value)>(capacity);
        foreach (var principalId in query.PrincipalIds)
        {
            names.Add((true, principalId));
        }

        foreach (var userName in query.UserNames)
        {
            names.Add((false, userName));
        }

        var results = new List<PrincipalSummaryResource>();
        for (var start = 0; start < names.Count; start += ControlPlanePrincipalQuery.MaxNames)
        {
            var count = Math.Min(ControlPlanePrincipalQuery.MaxNames, names.Count - start);
            var chunk = names.GetRange(start, count);
            var chunkIds = new List<string>(count);
            var chunkUserNames = new List<string>(count);
            foreach (var (isId, value) in chunk)
            {
                if (isId)
                    chunkIds.Add(value);
                else
                    chunkUserNames.Add(value);
            }

            var chunkQuery = new ControlPlanePrincipalQuery
            {
                PrincipalIds = chunkIds,
                UserNames = chunkUserNames,
            };
            var principalsRequest = new CellApiRequest(
                HttpMethod.Post,
                principalsUri,
                GlobalDirectoryProtocol.ControlPlaneAudience,
                GlobalDirectoryProtocol.ControlPlaneScope
            )
            {
                Body = chunkQuery,
            };
            var response = await Client.SendAsync(principalsRequest, cancellationToken);

            response.EnsureSuccess("describe control-plane principals");
            var page = response.Read<CollectionResource<PrincipalSummaryResource>>();
            results.AddRange(page.Items);
        }

        return results;
    }
}
