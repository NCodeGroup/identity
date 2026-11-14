// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Validates the management access token of a request. A cell validates the token against its own tenants, or against
/// the control plane's issuer when the control plane is another cell.
/// </summary>
[PublicAPI]
public interface IManagementTokenValidator
{
    /// <summary>
    /// Validates the management access token that the request carries.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The <see cref="ManagementTokenValidation"/> that tells whether the token authenticates, does not authenticate, or
    /// could not be validated because the authority that validates it is unavailable.
    /// </returns>
    ValueTask<ManagementTokenValidation> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    );
}
