// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Provides the default implementation of <see cref="ITenantProvisioner"/>. It creates a tenant at the address its
/// strategy derives for it, judged by the same rules as every other tenant written, and it seeds a tenant — after the
/// root tenant its strategy names — by fanning out <see cref="SeedTenantCommand"/> to every registered handler within a
/// single unit of work, then runs the steps the handlers queued for after the commit, each on its own bounded token; a
/// step that fails is logged, the remaining steps run, and the seed stands, because its unit of work has committed. It
/// then reads the tenant again in a fresh unit of work and returns the row its seed committed. Only the control-plane
/// cell seeds its root tenant in the root plane; every other tenant, and the root tenant of every other cell, is seeded
/// in the workload plane. A tenant it creates that a strategy serves (one with an issuer), other than the control-plane
/// cell's root tenant, is reserved and placed in this cell through the global directory, as the management API creates
/// a tenant; a tenant of the platform's own team is reserved in no team; a tenant without an issuer is created in this
/// cell alone.
/// </summary>
// See ADR-1166 (one seeding pipeline) and ADR-1351 (an effect that follows a committed change runs on its own bounded
// token).
internal sealed class DefaultTenantProvisioner(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantResolver tenantResolver,
    IOptions<TenantResolutionOptions> optionsAccessor,
    IOptions<CellOptions> cellOptionsAccessor,
    IMediator mediator,
    ITeamProvisioner teamProvisioner,
    ITenantDirectory tenantDirectory,
    TimeProvider timeProvider,
    ILogger<DefaultTenantProvisioner> logger
) : ITenantProvisioner
{
    // One retry is enough to converge after losing a race to a concurrent seed of the same tenant (see SeedAsync).
    private const int MaxSeedAttempts = 2;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ITenantResolver TenantResolver { get; } = tenantResolver;

    /// <summary>
    /// Gets the options of the cell that the host runs.
    /// </summary>
    private CellOptions CellOptions { get; } = cellOptionsAccessor.Value;
    private IMediator Mediator { get; } = mediator;
    private ITeamProvisioner TeamProvisioner { get; } = teamProvisioner;

    /// <summary>
    /// Gets the global directory that reserves and places a tenant this provisioner creates.
    /// </summary>
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DefaultTenantProvisioner> Logger { get; } = logger;

    private RootTenantOptions RootOptions => optionsAccessor.Value.Root;

    /// <inheritdoc />
    public async ValueTask<PersistedTenant> EnsureCreatedAsync(
        string teamId,
        string tenantId,
        string displayName,
        CancellationToken cancellationToken
    )
    {
        // The tenant's team exists before the tenant, so the tenant's foreign key to it holds (ADR-1411).
        var isRootTeam = string.Equals(
            teamId,
            RootOptions.TeamId,
            StringComparison.OrdinalIgnoreCase
        );
        await TeamProvisioner.EnsureCreatedAsync(
            teamId,
            isRootTeam ? RootOptions.TeamDisplayName : teamId,
            cancellationToken
        );

        // The tenant is created, and judged against every other tenant's address, outside any tenant scope.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped("tenant provisioning");
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITenantStore>();

        var existing = await store.GetOrDefaultAsync(tenantId, cancellationToken);
        if (existing is not null)
            return existing;

        // A tenant created on demand is written like any other: at the address its strategy derives for it, judged by
        // the rules every tenant write applies (ADR-1160, ADR-1165).
        var issuer = await TenantResolver.GetDefaultIssuerAsync(tenantId, cancellationToken);
        var mtlsEndpointAliasAuthority =
            await TenantResolver.GetDefaultMtlsEndpointAliasAuthorityAsync(
                tenantId,
                cancellationToken
            );
        var failures = await TenantResolver.ValidateTenantAsync(
            tenantId,
            issuer,
            mtlsEndpointAliasAuthority,
            storeManager,
            cancellationToken
        );
        if (failures.Count > 0)
        {
            var failureDetails = failures.Select(failure => failure.Detail);
            var details = string.Join(" ", failureDetails);
            throw new InvalidOperationException(
                $"The tenant '{tenantId}' cannot be created at its default address: {details}"
            );
        }

        var persistedTenant = CreateEmptyPersistedTenant(
            teamId,
            tenantId,
            displayName,
            issuer,
            mtlsEndpointAliasAuthority
        );

        // The control-plane cell creates its root tenant without the directory, which accepts only the tokens that
        // tenant issues; every other tenant a strategy serves is reserved, inserted and placed through the directory,
        // as the management API creates a tenant (ADR-1160, ADR-1411).
        var isDirectoryTokenIssuer = RootTenantRules.IsDirectoryTokenIssuer(
            CellOptions,
            TenantResolver,
            tenantId
        );
        if (isDirectoryTokenIssuer)
        {
            var concurrentRoot = await InsertOrFindConcurrentAsync(
                persistedTenant,
                storeManager,
                cancellationToken
            );
            return concurrentRoot ?? persistedTenant;
        }

        // A tenant no strategy serves has no address on the platform, so it holds nothing in the directory: its
        // identifier, such as a workload cell's root tenant `root`, can be the one the directory holds for the
        // control-plane cell's root tenant.
        if (persistedTenant.Issuer is null)
        {
            var concurrentUnserved = await InsertOrFindConcurrentAsync(
                persistedTenant,
                storeManager,
                cancellationToken
            );
            return concurrentUnserved ?? persistedTenant;
        }

        return await ReserveInsertAndPlaceAsync(persistedTenant, storeManager, cancellationToken);
    }

    /// <summary>
    /// Inserts the tenant and commits it.
    /// </summary>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>null</c> when the
    /// tenant is inserted, or the row a concurrent creator wrote when the insert lost to it.</returns>
    private async ValueTask<PersistedTenant?> InsertOrFindConcurrentAsync(
        PersistedTenant tenant,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<ITenantStore>();
        await store.AddAsync(tenant, cancellationToken);

        try
        {
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Another node or request may have created the tenant between the read above and this write — two replicas
            // reaching the root tenant on a fresh deployment, say. The row it wrote is the tenant; anything else is a
            // genuine failure.
            var concurrent = await FindTenantAsync(tenant.TenantId, cancellationToken);
            if (concurrent is null)
                throw;

            return concurrent;
        }

        return null;
    }

    /// <summary>
    /// Creates a tenant through the directory as a saga: reserves the tenant's identifier and issuer, inserts the
    /// tenant, then places it in this cell. A creation that fails before the tenant is placed removes the inserted
    /// tenant and then releases the reservation, each on its own bounded token, and keeps the reservation when the
    /// removal fails, so the directory never frees an identifier whose row still exists; a compensating step that fails
    /// is logged, and the failure that ended the creation propagates.
    /// </summary>
    private async ValueTask<PersistedTenant> ReserveInsertAndPlaceAsync(
        PersistedTenant tenant,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        // A tenant of the platform's own team is reserved in no team, because the directory holds no record of that
        // team (ADR-1411).
        var reservedTeamId = TenantReservationRules.GetTeamId(tenant.TeamId, RootOptions.TeamId);
        var reservation = new TenantReservation
        {
            TenantId = tenant.TenantId,
            Issuer = tenant.Issuer,
            TeamId = reservedTeamId,
            Cell = CellOptions.Name,
        };
        var reservationResult = await TenantDirectory.ReserveAsync(reservation, cancellationToken);
        if (!reservationResult.IsReserved)
        {
            // Replicas race to create the same tenant, and the directory's reservation is idempotent for the same
            // cell, issuer and team; a refusal that follows a concurrent creator's row is that creator's tenant.
            var creator = await FindTenantAsync(tenant.TenantId, cancellationToken);
            if (creator is not null)
                return creator;

            throw new InvalidOperationException(
                $"The tenant '{tenant.TenantId}' cannot be created: {reservationResult.ConflictDetail}"
            );
        }

        var isInserted = false;
        var isSettled = false;
        try
        {
            var concurrent = await InsertOrFindConcurrentAsync(
                tenant,
                storeManager,
                cancellationToken
            );
            if (concurrent is not null)
            {
                // The concurrent creator holds the same reservation and places the tenant, so nothing is released.
                isSettled = true;
                return concurrent;
            }

            isInserted = true;

            var placementRequest = new TenantPlacementRequest
            {
                TenantId = tenant.TenantId,
                Issuer = tenant.Issuer,
                Cell = CellOptions.Name,
            };
            var placementResult = await TenantDirectory.PlaceAsync(
                placementRequest,
                cancellationToken
            );
            if (!placementResult.IsReserved)
            {
                throw new InvalidOperationException(
                    $"The tenant '{tenant.TenantId}' cannot be placed in this cell: {placementResult.ConflictDetail}"
                );
            }

            isSettled = true;
            return tenant;
        }
        finally
        {
            if (!isSettled)
            {
                // The compensation runs even when the creation was canceled, each step on its own token bounded as work
                // that follows a commit, and a step that fails or outlasts its bound is logged, so the failure that
                // ended the creation surfaces. The reservation is released only once the inserted row is gone, so the
                // directory never frees an identifier whose row still exists; a reservation kept for a row that a
                // timed-out removal did remove is reserved again by the next creation of the tenant in this cell.
                // See ADR-1351 and ADR-1411.
                var isRemoved = !isInserted;
                if (isInserted)
                {
                    try
                    {
                        await PostCommitWork.RunAsync(
                            TimeProvider,
                            RemoveCreatedTenantAsync,
                            tenant.TenantId
                        );
                        isRemoved = true;
                    }
                    catch (Exception exception)
                    {
                        Logger.TenantCreationRemovalFailed(tenant.TenantId, exception);
                    }
                }

                if (isRemoved)
                {
                    try
                    {
                        await PostCommitWork.RunAsync(
                            TimeProvider,
                            TenantDirectory.ReleaseAsync,
                            tenant.TenantId
                        );
                    }
                    catch (Exception exception)
                    {
                        Logger.TenantCreationReleaseFailed(tenant.TenantId, exception);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes a tenant whose creation failed after its row committed, on a store manager of its own, so the creation's
    /// unit of work takes no part in it.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    private async ValueTask RemoveCreatedTenantAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var cleanup = await StoreManagerFactory.CreateAsync(cancellationToken);
        var cleanupStore = cleanup.GetStore<ITenantStore>();
        await cleanupStore.RemoveFromCellAsync(tenantId, cancellationToken);
        await cleanup.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PersistedTenant> SeedAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var rootTenantId = TenantResolver.GetControlPlaneTenantId();
        var isRootTenant = string.Equals(tenantId, rootTenantId, StringComparison.Ordinal);

        var rootTenantPlane = CellOptions.IsControlPlane ? TenantPlane.Root : TenantPlane.Workload;

        // The root tenant is the control-plane tenant that the strategy names; under the static single-tenant strategy it
        // is the strategy's own tenant, which is then seeded once. It must exist and be seeded whatever tenant is
        // seeded, on this same path, so root and workload tenants are seeded identically (ADR-1166). Only the
        // control-plane cell seeds it in the root plane, which holds the control plane's data: every other cell
        // validates management tokens against the control-plane cell and maps no platform or team route, so it seeds
        // its root tenant in the workload plane (ADR-1411).
        if (!isRootTenant)
        {
            var rootDisplayName = TenantResolver.GetControlPlaneTenantDisplayName();
            await EnsureCreatedAsync(
                RootOptions.TeamId,
                rootTenantId,
                rootDisplayName,
                cancellationToken
            );
            await SeedInUnitOfWorkAsync(rootTenantId, rootTenantPlane, cancellationToken);
        }

        await SeedInUnitOfWorkAsync(
            tenantId,
            isRootTenant ? rootTenantPlane : TenantPlane.Workload,
            cancellationToken
        );

        // The seed's contributors may write the tenant row, which issues it new concurrency tokens, so the tenant is read
        // again once the seed has committed and its steps have run; a tenant materialized from it carries the tokens of
        // the row its own seed committed (ADR-1166).
        return await FindTenantAsync(tenantId, cancellationToken)
            ?? throw TenantErrors.NotFound(tenantId);
    }

    /// <summary>
    /// Seeds the specified tenant in one unit of work, retries a commit that another replica's write lost, and runs the
    /// steps queued for after the commit, without reading the committed row.
    /// </summary>
    private async ValueTask SeedInUnitOfWorkAsync(
        string tenantId,
        TenantPlane plane,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; ++attempt)
        {
            // Scope the whole seed to this tenant so every seed handler's tenant-bound queries resolve to it.
            using var tenantScope = AmbientTenantAccessor.BeginScope(tenantId);
            await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
            var store = storeManager.GetStore<ITenantStore>();

            var persistedTenant =
                await store.GetOrDefaultAsync(tenantId, cancellationToken)
                ?? throw TenantErrors.NotFound(tenantId);

            var context = new TenantSeedContext
            {
                Tenant = persistedTenant,
                Plane = plane,
                StoreManager = storeManager,
            };

            // Every seed handler runs and enlists in the shared unit of work; the seeded data commits together below.
            var seedCommand = new SeedTenantCommand(context);
            await Mediator.SendAsync(seedCommand, cancellationToken);

            try
            {
                await storeManager.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException && attempt < MaxSeedAttempts)
            {
                // Another seed of the same tenant — a second replica, or a second request that missed the tenant cache —
                // may have committed between this seed's reads and its write. Seed contributors are idempotent, so
                // seeding again in a fresh unit of work observes what that seed wrote and adds only what is still
                // missing, and the steps queued for after this attempt's commit never run. A failure that persists is
                // genuine and surfaces from the last attempt.
                continue;
            }

            // The seed is committed, so each effect that follows it runs on its own bounded token: the request's token
            // reaches none of them, and a disconnect cannot leave the seed committed without its effects. No caller
            // token reaches a step, so every failure, its timeout included, is logged, and the remaining steps run:
            // the seed has committed, and a tenant whose keys are in the store must materialize.
            foreach (var step in context.PostCommitSteps)
            {
                try
                {
                    await PostCommitWork.RunAsync(
                        TimeProvider,
                        static (step, token) => step(token),
                        step
                    );
                }
                catch (Exception exception)
                {
                    Logger.TenantSeedPostCommitStepFailed(tenantId, exception);
                }
            }

            return;
        }
    }

    private async ValueTask<PersistedTenant?> FindTenantAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        // The seed reads the tenant back before any tenant scope exists.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped("tenant lookup");
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITenantStore>();
        return await store.GetOrDefaultAsync(tenantId, cancellationToken);
    }

    private static PersistedTenant CreateEmptyPersistedTenant(
        string teamId,
        string tenantId,
        string displayName,
        string? issuer,
        string? mtlsEndpointAliasAuthority
    )
    {
        var settings = new PersistedTenantSettings
        {
            TenantId = tenantId,
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
            Value = JsonElements.EmptyObject,
        };

        var secrets = new PersistedTenantSecrets
        {
            TenantId = tenantId,
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
            Value = [],
        };

        return new PersistedTenant
        {
            TenantId = tenantId,
            TeamId = teamId,
            // The tenant's request-independent identity; only a tenant its strategy does not serve has none.
            Issuer = issuer,
            MtlsEndpointAliasAuthority = mtlsEndpointAliasAuthority,
            ConcurrencyToken = Guid.NewGuid().ToString("N"),
            IsDisabled = false,
            DisplayName = displayName,
            Settings = settings,
            Secrets = secrets,
        };
    }
}
