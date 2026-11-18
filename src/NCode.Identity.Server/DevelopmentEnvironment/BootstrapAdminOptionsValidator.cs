// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.Server.DevelopmentEnvironment;

/// <summary>
/// Validates <see cref="BootstrapAdminOptions"/> when the bootstrap administrator is configured: it must name the one
/// tenant it is seeded into, and that tenant must be the control-plane tenant, where the platform role assignment is
/// read. Without a tenant, every tenant — including each one created at runtime — would receive a client that holds the
/// <c>GlobalAdmin</c> role.
/// </summary>
// See ADR-1137 (fail at startup) and ADR-1166 (a seeder that names a tenant seeds exactly that tenant).
internal sealed class BootstrapAdminOptionsValidator(ITenantResolver tenantResolver)
    : IValidateOptions<BootstrapAdminOptions>
{
    private ITenantResolver TenantResolver { get; } = tenantResolver;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BootstrapAdminOptions options)
    {
        if (!options.IsEnabled)
            return ValidateOptionsResult.Success;

        if (string.IsNullOrEmpty(options.TenantId))
        {
            return ValidateOptionsResult.Fail(
                $"{BootstrapAdminOptions.DefaultSectionName}:{nameof(options.TenantId)} is required when the bootstrap "
                    + "administrator is configured: name the one tenant its client is seeded into."
            );
        }

        var controlPlaneTenantId = TenantResolver.GetControlPlaneTenantId();
        if (!string.Equals(options.TenantId, controlPlaneTenantId, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"{BootstrapAdminOptions.DefaultSectionName}:{nameof(options.TenantId)} must name the control-plane "
                    + $"tenant '{controlPlaneTenantId}', where the bootstrap administrator's platform role is read."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
