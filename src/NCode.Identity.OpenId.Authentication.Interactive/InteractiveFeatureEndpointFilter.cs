// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Provides an <see cref="IEndpointFilter"/> that runs the endpoints of an optional feature of the interactive surface
/// only while the request's tenant turns the feature on, and otherwise answers 404, so a page or endpoint of a feature
/// the tenant has off does not exist.
/// </summary>
/// <param name="settingKey">The tenant setting that turns the feature on.</param>
// See ADR-1533.
internal sealed class InteractiveFeatureEndpointFilter(SettingKey<bool> settingKey)
    : IEndpointFilter
{
    private SettingKey<bool> SettingKey { get; } = settingKey;

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var settings = context.HttpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
        if (settings.GetValue(SettingKey))
        {
            return next(context);
        }

        IResult notFound = TypedResults.NotFound();
        return ValueTask.FromResult<object?>(notFound);
    }
}
