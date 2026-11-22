// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using NCode.Buffers;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The <see cref="ICookieManager"/> of the interactive session cookie. The cookie is host-only (<c>Path=/</c>, no
/// <c>Domain</c>, <c>Secure</c>), and tenants that share an origin under the path strategy are separated by name: a
/// tenant served under a path gets a cookie name qualified by a hash of that path.
/// </summary>
// See ADR-1193.
internal sealed class InteractiveSessionCookieManager(InteractiveSurfacePaths surfacePaths)
    : ICookieManager
{
    private InteractiveSurfacePaths SurfacePaths { get; } = surfacePaths;
    private ChunkingCookieManager Inner { get; } = new();

    /// <inheritdoc />
    public string? GetRequestCookie(HttpContext context, string key)
    {
        var cookieName = GetTenantCookieName(context, key);
        return Inner.GetRequestCookie(context, cookieName);
    }

    /// <inheritdoc />
    public void AppendResponseCookie(
        HttpContext context,
        string key,
        string? value,
        CookieOptions options
    )
    {
        var cookieName = GetTenantCookieName(context, key);
        var hostOnlyOptions = ToHostOnly(options);
        Inner.AppendResponseCookie(context, cookieName, value, hostOnlyOptions);
    }

    /// <inheritdoc />
    public void DeleteCookie(HttpContext context, string key, CookieOptions options)
    {
        var cookieName = GetTenantCookieName(context, key);
        var hostOnlyOptions = ToHostOnly(options);
        Inner.DeleteCookie(context, cookieName, hostOnlyOptions);
    }

    private string GetTenantCookieName(HttpContext context, string key)
    {
        var tenantPath = SurfacePaths.GetTenantPath(context);
        if (!tenantPath.HasValue)
        {
            return key;
        }

        var tenantPathBytes = SecureEncoding.UTF8.GetBytes(tenantPath.Value);
        var hash = SHA256.HashData(tenantPathBytes);
        return $"{key}.{Base64Url.EncodeToString(hash)}";
    }

    private static CookieOptions ToHostOnly(CookieOptions options) =>
        new(options)
        {
            Path = "/",
            Domain = null,
            Secure = true,
        };
}
