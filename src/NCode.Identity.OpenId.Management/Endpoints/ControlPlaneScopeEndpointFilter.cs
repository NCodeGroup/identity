// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// The endpoint filter for the control-plane management routes (capabilities, caller, platform, teams, machine clients
/// and tenant provisioning), which act on rows of many tenants. It opens the ambient unscoped scope for the length of
/// the request, so the persistence-layer query filter lets those routes read across tenants; a handler that narrows a
/// read to one tenant opens its own tenant scope inside it. It is added to each control-plane route family and never to
/// the root management group, so the tenant-plane group, which keeps <see cref="TenantScopeEndpointFilter"/>, never
/// runs unscoped.
/// </summary>
internal sealed class ControlPlaneScopeEndpointFilter(IAmbientTenantAccessor ambientTenantAccessor)
    : IEndpointFilter
{
    /// <summary>
    /// The reason that the filter gives the unscoped scope it opens.
    /// </summary>
    public const string UnscopedReason = "management control plane";

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        using var scope = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        return await next(context);
    }
}
