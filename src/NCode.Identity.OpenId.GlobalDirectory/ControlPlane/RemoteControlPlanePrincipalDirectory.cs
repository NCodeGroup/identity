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

        var principalsUri = CreatePrincipalsUri();
        var names = new List<(bool IsId, string Value)>();
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
            var chunk = names.Skip(start).Take(ControlPlanePrincipalQuery.MaxNames).ToList();
            var chunkQuery = new ControlPlanePrincipalQuery
            {
                PrincipalIds = chunk.Where(name => name.IsId).Select(name => name.Value).ToList(),
                UserNames = chunk.Where(name => !name.IsId).Select(name => name.Value).ToList(),
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

    /// <summary>
    /// Creates the URI of the control plane's principals endpoint from the control plane's issuer.
    /// </summary>
    /// <returns>The absolute URI of the principals endpoint on the control plane's host.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell does not know an absolute issuer of the control plane.
    /// </exception>
    private Uri CreatePrincipalsUri()
    {
        var issuer = CellOptions.ControlPlane.Issuer;
        if (!AbsoluteUri.TryParse(issuer, out var uri))
            throw new DirectoryUnavailableException(
                "The cell does not know the issuer of the control plane."
            );

        var authority = uri.GetLeftPart(UriPartial.Authority);
        return new Uri(authority + DirectoryClientDefaults.PrincipalsPath);
    }
}
