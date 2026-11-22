// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Provides a default implementation of the <see cref="ITenantResolver"/> abstraction that selects the configured
/// <see cref="ITenantStrategy"/> by <see cref="TenantResolutionOptions.StrategyCode"/> and delegates to it. It
/// validates the addressing rules every strategy shares itself — the root tenant's address among them — and the
/// selected strategy validates its own and derives a tenant's default issuer. The lookups that find a tenant by its
/// host or issuer, and the validation lookups, read across tenants, so each runs in an unscoped scope. The identifiers
/// and issuers of deleted tenants are answered by the <see cref="ITenantDirectory"/>.
/// </summary>
internal class DefaultTenantResolver(
    IOptions<TenantResolutionOptions> optionsAccessor,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantDirectory tenantDirectory,
    IEnumerable<ITenantStrategy> strategies
) : ITenantResolver
{
    private TenantResolutionOptions Options { get; } = optionsAccessor.Value;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;

    private ImmutableArray<ITenantStrategy> Strategies { get; } = [.. strategies];

    private ITenantStrategy? SelectedOrNull { get; set; }

    private ITenantStrategy Selected => SelectedOrNull ??= SelectStrategy();

    private ITenantStrategy SelectStrategy()
    {
        var strategyCode = Options.StrategyCode;
        return Strategies.FirstOrDefault(strategy =>
                string.Equals(strategyCode, strategy.StrategyCode, StringComparison.Ordinal)
            )
            ?? throw new InvalidOperationException(
                $"Unable to find a tenant strategy with code '{strategyCode}'."
            );
    }

    /// <inheritdoc />
    public RoutePattern GetTenantRoute() => Selected.GetTenantRoute();

    /// <inheritdoc />
    public bool TryGetTenantId(HttpContext httpContext, [NotNullWhen(true)] out string? tenantId) =>
        Selected.TryGetTenantId(httpContext, out tenantId);

    /// <inheritdoc />
    public async ValueTask<PersistedTenant> ResolveTenantAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        // The tenant is found by the host or issuer of the request before any tenant scope exists.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped("tenant resolution");
        return await Selected.ResolveTenantAsync(httpContext, cancellationToken);
    }

    /// <inheritdoc />
    public string GetControlPlaneTenantId() => Selected.GetControlPlaneTenantId();

    /// <inheritdoc />
    public async ValueTask<PersistedTenant> ResolveControlPlaneTenantAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        // The control-plane tenant is found before any tenant scope exists.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped("control-plane tenant resolution");
        return await Selected.ResolveControlPlaneTenantAsync(httpContext, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetDefaultIssuerAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        // A strategy derives an issuer from a well-formed identifier only; a malformed one is reported by validation.
        if (!TenantAddressRules.IsValidTenantId(tenantId))
            return null;

        return await Selected.GetDefaultIssuerAsync(tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetDefaultMtlsEndpointAliasAuthorityAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        // A strategy derives an alias from a well-formed identifier only; a malformed one is reported by validation.
        if (!TenantAddressRules.IsValidTenantId(tenantId))
            return null;

        return await Selected.GetDefaultMtlsEndpointAliasAuthorityAsync(
            tenantId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public bool IsRequiredTenant(string tenantId) => Selected.IsRequiredTenant(tenantId);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TenantValidationFailure>> ValidateTenantAsync(
        string tenantId,
        string? issuer,
        string? mtlsEndpointAliasAuthority,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var failures = new List<TenantValidationFailure>();

        if (!TenantAddressRules.IsValidTenantId(tenantId))
        {
            var invalidTenantIdFailure = new TenantValidationFailure
            {
                Detail =
                    $"The tenant identifier '{tenantId}' must be {TenantAddressRules.TenantIdRequirement}.",
            };
            failures.Add(invalidTenantIdFailure);
        }

        Uri? issuerUri = null;
        if (
            !string.IsNullOrEmpty(issuer)
            && !TenantAddressRules.TryParseIssuer(issuer, out issuerUri)
        )
        {
            var invalidIssuerFailure = new TenantValidationFailure
            {
                Detail = $"The issuer '{issuer}' must be {TenantAddressRules.IssuerRequirement}.",
            };
            failures.Add(invalidIssuerFailure);
        }

        Uri? mtlsEndpointAliasAuthorityUri = null;
        if (
            !string.IsNullOrEmpty(mtlsEndpointAliasAuthority)
            && !TenantAddressRules.TryParseMtlsEndpointAliasAuthority(
                mtlsEndpointAliasAuthority,
                out mtlsEndpointAliasAuthorityUri
            )
        )
        {
            var invalidAuthorityFailure = new TenantValidationFailure
            {
                Detail =
                    $"The mTLS endpoint alias authority '{mtlsEndpointAliasAuthority}' must be "
                    + $"{TenantAddressRules.MtlsEndpointAliasAuthorityRequirement}.",
            };
            failures.Add(invalidAuthorityFailure);
        }

        // The remaining rules, the strategy's included, are only meaningful for a well-formed identifier and issuer.
        if (failures.Count > 0)
            return failures;

        var store = storeManager.GetStore<ITenantStore>();

        // The identifier, issuer and host rules hold across every tenant, so the lookups read across tenants.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped("tenant address validation");

        // A tenant identifier names its tenant in every route and management call, so one that belonged to a deleted
        // tenant is never reissued (ADR-1160).
        if (await TenantDirectory.IsTenantIdRetiredAsync(tenantId, cancellationToken))
        {
            var retiredTenantIdFailure = new TenantValidationFailure
            {
                Detail =
                    $"The tenant identifier '{tenantId}' belonged to a deleted tenant and is never reissued.",
                IsConflict = true,
            };
            failures.Add(retiredTenantIdFailure);
        }

        // The issuer is the tenant's identity and is asserted in every token it mints, so two tenants sharing one
        // would mint tokens a relying party cannot tell apart (ADR-1165).
        if (issuerUri is not null)
        {
            var holder = await store.GetOrDefaultByIssuerAsync(
                issuerUri.OriginalString,
                cancellationToken
            );
            if (
                holder is not null
                && !string.Equals(holder.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            )
            {
                var duplicateIssuerFailure = new TenantValidationFailure
                {
                    Detail = $"Another tenant already has the issuer '{issuer}'.",
                    IsConflict = true,
                };
                failures.Add(duplicateIssuerFailure);
            }
            else if (
                await TenantDirectory.GetRetiredIssuerHolderOrDefaultAsync(
                    issuerUri.OriginalString,
                    cancellationToken
                )
                    is { } formerHolder
                && !string.Equals(formerHolder, tenantId, StringComparison.OrdinalIgnoreCase)
            )
            {
                // Relying parties that trusted a retired issuer would accept another tenant's tokens under it, so only
                // the tenant that gave it up can take it back (ADR-1160).
                var retiredIssuerFailure = new TenantValidationFailure
                {
                    Detail =
                        $"The issuer '{issuer}' belonged to another tenant and is never reissued.",
                    IsConflict = true,
                };
                failures.Add(retiredIssuerFailure);
            }
            else if (await IsRootTenantAddressAsync(tenantId, issuerUri, cancellationToken))
            {
                // The root tenant is created on demand at its configured address, so a tenant written there first would
                // take the control plane's address and leave the root tenant unable to be created (ADR-1165).
                var rootTenantAddressFailure = new TenantValidationFailure
                {
                    Detail = $"The issuer '{issuer}' is reserved for the root tenant.",
                    IsConflict = true,
                };
                failures.Add(rootTenantAddressFailure);
            }
        }

        var strategyFailures = await Selected.ValidateTenantAsync(
            tenantId,
            issuerUri,
            mtlsEndpointAliasAuthorityUri,
            storeManager,
            cancellationToken
        );
        failures.AddRange(strategyFailures);

        return failures;
    }

    private async ValueTask<bool> IsRootTenantAddressAsync(
        string tenantId,
        Uri issuer,
        CancellationToken cancellationToken
    )
    {
        var rootTenantId = Options.Root.TenantId;
        if (string.Equals(tenantId, rootTenantId, StringComparison.OrdinalIgnoreCase))
            return false;

        var rootIssuer = await Selected.GetDefaultIssuerAsync(rootTenantId, cancellationToken);
        return AbsoluteUri.TryParse(rootIssuer, out var rootIssuerUri)
            && Uri.Compare(
                issuer,
                rootIssuerUri,
                UriComponents.AbsoluteUri,
                UriFormat.UriEscaped,
                StringComparison.OrdinalIgnoreCase
            ) == 0;
    }
}
