// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// Reads what a management caller's token says about who issued it.
/// </summary>
internal static class ManagementCallerClaims
{
    extension(ClaimsPrincipal principal)
    {
        /// <summary>
        /// Gets a value that indicates whether the cell's own management tenant issued the caller's token, so the cell
        /// stores everything that authorizes the caller.
        /// </summary>
        public bool IsCellIssued
        {
            get
            {
                var issuerKind = principal.FindFirstValue(ManagementIssuerKinds.ClaimType);
                return string.Equals(
                    issuerKind,
                    ManagementIssuerKinds.Cell,
                    StringComparison.Ordinal
                );
            }
        }
    }
}
