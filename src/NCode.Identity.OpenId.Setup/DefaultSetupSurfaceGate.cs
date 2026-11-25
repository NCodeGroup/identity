// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.AspNetCore.Http;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Provides a default implementation of the <see cref="ISetupSurfaceGate"/> abstraction. The surface is served while
/// the instance is not initialized, and only over HTTPS. When <see cref="LocalDevelopmentMode.Enabled"/> is set, a
/// plain HTTP request is also served if it comes from a loopback remote address; a request without a remote address is
/// not loopback. A request that is neither is refused without reading the instance. Setup belongs to a cell whose
/// anchor tenant takes its first administrator, the control-plane cell or a cell with a management tenant, so any other
/// cell never serves the surface.
/// </summary>
internal sealed class DefaultSetupSurfaceGate(
    IInstanceSetupService instanceSetupService,
    LocalDevelopmentMode localDevelopment,
    IPlatformTopology platformTopology
) : ISetupSurfaceGate
{
    private IInstanceSetupService InstanceSetupService { get; } = instanceSetupService;

    private LocalDevelopmentMode LocalDevelopment { get; } = localDevelopment;

    private IPlatformTopology PlatformTopology { get; } = platformTopology;

    /// <inheritdoc />
    public async ValueTask<bool> IsServedAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!PlatformTopology.IsControlPlaneCell && !PlatformTopology.HasManagementTenant)
        {
            return false;
        }

        if (!IsTransportAdmitted(httpContext))
        {
            return false;
        }

        return !await InstanceSetupService.IsInitializedAsync(cancellationToken);
    }

    private bool IsTransportAdmitted(HttpContext httpContext)
    {
        if (httpContext.Request.IsHttps)
        {
            return true;
        }

        return LocalDevelopment.Enabled
            && httpContext.Connection.RemoteIpAddress is { } address
            && IPAddress.IsLoopback(address);
    }
}
