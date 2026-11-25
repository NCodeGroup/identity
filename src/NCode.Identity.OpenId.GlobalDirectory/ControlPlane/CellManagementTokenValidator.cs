// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Validates the management token of a request on a cell that has a management tenant of its own. A token whose
/// unverified issuer is the control plane's goes to the validator of the replicated keys, and any other token goes to
/// the validator of the cell's own tenants, which verifies it against the tenant that the request selects. The
/// authenticated identity carries the kind of issuer, so authorization knows which authority applies.
/// </summary>
internal sealed class CellManagementTokenValidator(
    IManagementTokenValidator controlPlaneValidator,
    IManagementTokenValidator cellValidator,
    IOptions<CellOptions> cellOptions
) : IManagementTokenValidator
{
    private static string[] SchemePrefixes { get; } = ["DPoP ", "Bearer "];

    /// <summary>
    /// Reads the <c>iss</c> claim of a compact JWT without verifying it.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <returns>The issuer, or <c>null</c> when the request carries no readable token with an issuer.</returns>
    private static string? PeekIssuer(HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        foreach (var prefix in SchemePrefixes)
        {
            if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization[prefix.Length..].Trim();
                return UnverifiedJwt.TryReadIssuer(token, out var issuer) ? issuer : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Records which kind of issuer signed the token on the authenticated identity of the principal.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> that the validation produced.</param>
    /// <param name="kind">The kind of the issuer.</param>
    private static void SetIssuerKind(ClaimsPrincipal principal, string kind)
    {
        var identity = principal.Identities.FirstOrDefault(x => x.IsAuthenticated);
        if (identity is null)
        {
            identity = new ClaimsIdentity();
            principal.AddIdentity(identity);
        }

        foreach (var existing in identity.FindAll(ManagementIssuerKinds.ClaimType).ToArray())
            identity.RemoveClaim(existing);

        var claim = new Claim(ManagementIssuerKinds.ClaimType, kind);
        identity.AddClaim(claim);
    }

    private IManagementTokenValidator ControlPlaneValidator { get; } = controlPlaneValidator;
    private IManagementTokenValidator CellValidator { get; } = cellValidator;
    private string ControlPlaneIssuer { get; } =
        cellOptions.Value.ControlPlane.Issuer ?? string.Empty;

    /// <inheritdoc />
    public async ValueTask<ManagementTokenValidation> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var issuer = PeekIssuer(httpContext);
        if (issuer is null)
            return ManagementTokenValidation.Unauthenticated;

        var isControlPlane =
            !string.IsNullOrEmpty(ControlPlaneIssuer)
            && string.Equals(issuer, ControlPlaneIssuer, StringComparison.Ordinal);

        var validator = isControlPlane ? ControlPlaneValidator : CellValidator;
        var kind = isControlPlane ? ManagementIssuerKinds.ControlPlane : ManagementIssuerKinds.Cell;

        var validation = await validator.ValidateAsync(httpContext, cancellationToken);
        if (validation.Principal is not null)
            SetIssuerKind(validation.Principal, kind);

        return validation;
    }
}
