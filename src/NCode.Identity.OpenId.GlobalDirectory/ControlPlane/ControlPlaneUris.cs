// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Builds the URIs of the control plane's cell-only endpoints from the control plane's issuer.
/// </summary>
// See ADR-1411.
internal static class ControlPlaneUris
{
    /// <summary>
    /// Creates the URI of a control-plane endpoint at the origin of the control plane's issuer.
    /// </summary>
    /// <param name="cellOptions">The <see cref="CellOptions"/> that name the control plane's issuer.</param>
    /// <param name="path">The path of the endpoint, such as <see cref="GlobalDirectoryProtocol.ControlPlaneAuthorityPath"/>.</param>
    /// <returns>The absolute URI of the endpoint on the control plane's host.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell does not know an absolute issuer of the control plane.
    /// </exception>
    internal static Uri Create(CellOptions cellOptions, string path)
    {
        var issuer = cellOptions.ControlPlane.Issuer;
        if (!AbsoluteUri.TryParse(issuer, out var uri))
            throw new DirectoryUnavailableException(
                "The cell does not know the issuer of the control plane."
            );

        var authority = uri.GetLeftPart(UriPartial.Authority);
        return new Uri(authority + path);
    }
}
