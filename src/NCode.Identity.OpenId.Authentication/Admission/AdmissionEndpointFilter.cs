// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Authentication.Endpoints;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Results;
using NCode.Identity.OpenId.Settings;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// An endpoint filter, installed once on the OpenID endpoint group inside the environment filter and outside the
/// rate-limit filter, that sheds a metered request with <c>503 Service Unavailable</c> when this replica's in-flight
/// requests fill the share of its capacity the request's tenant may fill, before any rate limit counts it. A shed
/// request is neither handled, audited nor recorded as usage.
/// </summary>
// See ADR-1523.
internal sealed class AdmissionEndpointFilter(
    IRequestAdmissionGate admissionGate,
    IOptions<AdmissionOptions> optionsAccessor,
    AuthenticationMetrics metrics
) : IEndpointFilter
{
    private IRequestAdmissionGate AdmissionGate { get; } = admissionGate;

    private IOptions<AdmissionOptions> OptionsAccessor { get; } = optionsAccessor;

    private AuthenticationMetrics Metrics { get; } = metrics;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var httpContext = context.HttpContext;

        // The environment filter publishes the context; without it the request never entered the OpenID pipeline.
        var feature = httpContext.Features.Get<IOpenIdContextFeature>();
        if (feature is null)
            return await next(context);

        // Discovery and JWKS are unmetered: they are never counted and never shed.
        var isMetered =
            httpContext.GetEndpoint()?.Metadata.GetMetadata<IOpenIdEndpointMeteredMetadata>()
            is not { IsMetered: false };
        if (!isMetered)
            return await next(context);

        var openIdContext = feature.OpenIdContext;
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        var capacityPercent = settings.TryGetValue(
            OpenIdSettingKeys.AdmissionCapacityPercent,
            out var percent
        )
            ? percent
            : 100;

        if (!AdmissionGate.TryEnter(capacityPercent))
        {
            var environmentType = settings.GetValue(OpenIdSettingKeys.EnvironmentType);
            Metrics.RequestShed(environmentType);
            return AdmissionErrors
                .Shed(openIdContext, OptionsAccessor.Value.RetryAfter)
                .AsHttpResult();
        }

        try
        {
            return await next(context);
        }
        finally
        {
            AdmissionGate.Exit();
        }
    }
}
