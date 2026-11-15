// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Identifies the cell that calls the directory API. A cell authenticates as the machine client <c>cell-{name}</c>, so
/// the <c>client_id</c> claim of the request names the cell.
/// </summary>
// See ADR-1411.
internal static class DirectoryCaller
{
    /// <summary>
    /// Gets the name of the cell that made the request.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> of the request.</param>
    /// <returns>
    /// The name of the cell, which is the <c>client_id</c> claim without its <c>cell-</c> prefix; or <c>null</c> when
    /// the claim is missing, lacks the prefix or names no cell.
    /// </returns>
    public static string? GetCell(ClaimsPrincipal user)
    {
        var clientId = user.FindFirst(
            DirectoryAuthenticationEndpointFilter.ClientIdClaimType
        )?.Value;
        if (
            clientId is null
            || !clientId.StartsWith(DirectoryEndpoints.CellClientIdPrefix, StringComparison.Ordinal)
        )
        {
            return null;
        }

        var cell = clientId[DirectoryEndpoints.CellClientIdPrefix.Length..];
        return cell.Length == 0 ? null : cell;
    }

    /// <summary>
    /// Gets whether the request was made by the control-plane cell.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> of the request.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <returns><c>true</c> when the caller is the cell that <see cref="DirectoryServerOptions.ControlPlaneCell"/> names; otherwise, <c>false</c>.</returns>
    public static bool IsControlPlane(ClaimsPrincipal user, DirectoryServerOptions options) =>
        GetCell(user) is { } cell
        && string.Equals(cell, options.ControlPlaneCell, StringComparison.Ordinal);
}
