// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Brings tenants into existence and into a seeded state, independent of how they are later resolved. It creates the
/// tenants configuration defines (the root tenant, and a static strategy's own tenant) on demand, and it seeds any tenant
/// by fanning out <see cref="SeedTenantCommand"/> to every registered handler within a single unit of work, after
/// ensuring the root tenant exists and is seeded. Both operations are idempotent, so the server seeds every tenant it
/// materializes and the management API seeds every tenant it creates.
/// </summary>
[PublicAPI]
public interface ITenantProvisioner
{
    /// <summary>
    /// Ensures the tenant with the specified identifier exists, creating it when it does not with its strategy's
    /// default issuer (<see cref="ITenantResolver.GetDefaultIssuerAsync"/>), judged by
    /// <see cref="ITenantResolver.ValidateTenantAsync"/> like any other tenant written. It does not seed the tenant.
    /// Unless the tenant is the control-plane cell's root tenant, which the directory pins from its own configuration,
    /// a tenant a strategy serves (one with an issuer) is reserved and placed in this cell through the global directory
    /// before it is inserted, so its row never exists unplaced; a placement that fails releases the reservation, and an
    /// insert that fails leaves the tenant placed in this cell, which the next creation of the tenant completes; a
    /// tenant of the platform's own team is reserved in no team; a tenant without an issuer is created in this cell
    /// alone. A directory that cannot be reached fails the creation.
    /// </summary>
    /// <param name="teamId">The identifier of the team the tenant belongs to; the team is created when it does not exist.</param>
    /// <param name="tenantId">The identifier of the tenant to create.</param>
    /// <param name="displayName">The display name applied when the tenant is created; an existing tenant keeps its own.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the existing or newly
    /// created <see cref="PersistedTenant"/>.</returns>
    /// <exception cref="InvalidOperationException">The tenant does not exist and cannot be created at its default
    /// address, or the global directory refuses its reservation or its placement. A directory that cannot be reached
    /// throws <c>DirectoryUnavailableException</c>.</exception>
    ValueTask<PersistedTenant> EnsureCreatedAsync(
        string teamId,
        string tenantId,
        string displayName,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Brings the existing tenant with the specified identifier into a seeded state: it ensures the root tenant exists
    /// and is seeded first when the tenant is not itself the root tenant, then runs every seed contributor for the tenant
    /// within a single unit of work. A seed whose commit loses to a concurrent seed of the same tenant runs again in a
    /// fresh unit of work, so concurrent seeds converge on one seeded state.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to seed.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedTenant"/> as its seed committed it: read once the seed's unit of work has committed and the
    /// steps that follow it have run, so its row, settings and secrets concurrency tokens are those the seed wrote.</returns>
    ValueTask<PersistedTenant> SeedAsync(string tenantId, CancellationToken cancellationToken);
}
