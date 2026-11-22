// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Tenants.Strategies;

/// <summary>
/// Provides a tenant-selection strategy that always resolves the same single, statically-configured tenant, creating it
/// on first use when it does not yet exist. The configured tenant is the root (control-plane) tenant.
/// </summary>
internal class StaticSingleTenantStrategy(
    IStoreManagerFactory storeManagerFactory,
    IOptions<TenantResolutionOptions> optionsAccessor
) : TenantStrategy(storeManagerFactory, optionsAccessor)
{
    private StaticSingleTenantOptions Options =>
        ResolutionOptions.StaticSingle ?? new StaticSingleTenantOptions();

    /// <inheritdoc />
    public override string StrategyCode => OpenIdConstants.TenantStrategyCodes.StaticSingle;

    /// <inheritdoc />
    protected override PathString TenantPath => Options.TenantPath;

    private string ResolveConfiguredTenantId()
    {
        var tenantId = Options.TenantId;
        return string.IsNullOrEmpty(tenantId)
            ? StaticSingleTenantOptions.DefaultTenantId
            : tenantId;
    }

    private string ResolveConfiguredDisplayName()
    {
        var displayName = Options.DisplayName;
        return string.IsNullOrEmpty(displayName)
            ? StaticSingleTenantOptions.DefaultDisplayName
            : displayName;
    }

    private bool IsConfiguredTenant(string tenantId)
    {
        var configuredTenantId = ResolveConfiguredTenantId();
        return string.Equals(tenantId, configuredTenantId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool TryGetTenantId(
        HttpContext httpContext,
        [NotNullWhen(true)] out string? tenantId
    )
    {
        tenantId = ResolveConfiguredTenantId();
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The configured tenant's default issuer is its configured issuer. The strategy serves only its configured tenant,
    /// the root tenant, so it assigns no other tenant an issuer.
    /// </remarks>
    public override ValueTask<string?> GetDefaultIssuerAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var issuer = IsConfiguredTenant(tenantId) ? Options.Issuer : null;
        var defaultIssuer = string.IsNullOrEmpty(issuer) ? null : issuer;
        return ValueTask.FromResult(defaultIssuer);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The configured tenant's default alias is its configured alias. The strategy serves only its configured tenant,
    /// the root tenant, so it assigns no other tenant one.
    /// </remarks>
    public override ValueTask<string?> GetDefaultMtlsEndpointAliasAuthorityAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var alias = IsConfiguredTenant(tenantId) ? Options.MtlsEndpointAliasAuthority : null;
        var defaultAlias = string.IsNullOrEmpty(alias) ? null : alias;
        return ValueTask.FromResult(defaultAlias);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The strategy serves only the configured tenant, so only it needs an issuer.
    /// </remarks>
    protected override bool RequiresIssuer(string tenantId) => IsConfiguredTenant(tenantId);

    /// <inheritdoc />
    /// <remarks>
    /// The strategy serves only the configured tenant, so no other tenant can issue a token; the configured tenant is
    /// therefore the control-plane tenant.
    /// </remarks>
    public override string GetControlPlaneTenantId() => ResolveConfiguredTenantId();

    /// <inheritdoc />
    public override string GetControlPlaneTenantDisplayName() => ResolveConfiguredDisplayName();

    /// <inheritdoc />
    /// <remarks>
    /// The strategy serves only the configured tenant, so no other tenant can issue a token; the configured tenant is
    /// therefore the control-plane tenant.
    /// </remarks>
    public override async ValueTask<PersistedTenant> ResolveControlPlaneTenantAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    ) => await ResolveTenantAsync(httpContext, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask<PersistedTenant> ResolveTenantAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var tenantId = ResolveConfiguredTenantId();

        var displayName = GetControlPlaneTenantDisplayName();

        // The configured tenant is created on first resolution, with its configured issuer; the caller that
        // materializes it seeds it (ADR-1166). The provisioner is scoped (its mediator is request-scoped), so it is
        // resolved from the request's service provider.
        var provisioner = httpContext.RequestServices.GetRequiredService<ITenantProvisioner>();
        var persistedTenant = await provisioner.EnsureCreatedAsync(
            RootOptions.TeamId,
            tenantId,
            displayName,
            cancellationToken
        );

        return ThrowIfDisabled(persistedTenant);
    }
}
