// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Teams;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides an implementation of <see cref="ITenantPromotionContributor"/> that promotes the grants of the source
/// tenant's clients that the host does not manage. A grant is matched by its client and the resource server the
/// promotion maps for it, and the host-managed clients' grants stay out.
/// </summary>
// See ADR-1425.
internal class ClientGrantPromotionContributor(IHostManagedClientChecker hostManagedClientChecker)
    : ITenantPromotionContributor
{
    private IHostManagedClientChecker HostManagedClientChecker { get; } = hostManagedClientChecker;

    /// <inheritdoc />
    public int Order => TenantPromotionOrders.ClientGrants;

    /// <inheritdoc />
    public async ValueTask PlanAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var promotions = await ComputeAsync(context, cancellationToken);

        foreach (var promotion in promotions)
        {
            foreach (var detail in promotion.Errors)
            {
                context.AddError(
                    new TenantPromotionErrorResource
                    {
                        ResourceType = ManagementResourceTypes.ClientGrant,
                        ResourceId = promotion.ResourceId,
                        Detail = detail,
                    }
                );
            }

            if (promotion.TargetResourceServerId is null)
            {
                continue;
            }

            context.AddChange(
                new TenantPromotionChangeResource
                {
                    ResourceType = ManagementResourceTypes.ClientGrant,
                    ResourceId = promotion.ResourceId,
                    Action = promotion.Action,
                }
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var promotions = await ComputeAsync(context, cancellationToken);
        var store = context.StoreManager.GetStore<IClientGrantStore>();

        foreach (var promotion in promotions)
        {
            if (
                promotion.Action == TenantPromotionActions.Unchanged
                || promotion.TargetResourceServerId is not { } targetResourceServerId
            )
            {
                continue;
            }

            using var scope = context.BeginTargetScope();

            if (promotion.Target is null)
            {
                await store.AddAsync(
                    new PersistedClientGrant
                    {
                        TenantId = context.TargetTenant.TenantId,
                        ClientId = promotion.ClientId,
                        ResourceServerId = targetResourceServerId,
                        ConcurrencyToken = string.Empty,
                        Scopes = [.. promotion.Source.Scopes],
                    },
                    cancellationToken
                );
            }
            else
            {
                promotion.Target.Scopes = [.. promotion.Source.Scopes];
                await store.UpdateAsync(promotion.Target, cancellationToken);
            }
        }
    }

    private async ValueTask<List<ClientGrantPromotion>> ComputeAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var grantStore = context.StoreManager.GetStore<IClientGrantStore>();
        var resourceServerStore = context.StoreManager.GetStore<IResourceServerStore>();

        var sources = GetSourceGrants(context);
        var promotions = new List<ClientGrantPromotion>();

        foreach (var grant in sources)
        {
            var clientId = grant.ClientId;
            var errors = new List<string>();

            if (
                !context.TryGetTargetIdentifier(
                    ManagementResourceTypes.ResourceServer,
                    grant.ResourceServerId,
                    out var targetResourceServerId
                )
            )
            {
                errors.Add(
                    $"The resource server '{grant.ResourceServerId}' the grant reaches is neither promoted nor a system resource server of the target tenant."
                );

                promotions.Add(
                    new ClientGrantPromotion
                    {
                        ClientId = clientId,
                        Source = grant,
                        Target = null,
                        TargetResourceServerId = null,
                        Action = TenantPromotionActions.Unchanged,
                        Errors = errors,
                    }
                );
                continue;
            }

            using var scope = context.BeginTargetScope();

            var target = await grantStore.GetOrDefaultAsync(
                clientId,
                targetResourceServerId,
                cancellationToken
            );

            var targetResourceServer = await resourceServerStore.GetOrDefaultAsync(
                targetResourceServerId,
                cancellationToken
            );
            if (targetResourceServer is { IsSystem: true })
            {
                var definedScopes = targetResourceServer
                    .Scopes.Select(x => x.Value)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var scopeValue in grant.Scopes.Where(x => !definedScopes.Contains(x)))
                {
                    errors.Add(
                        $"The target tenant's system resource server '{targetResourceServer.Identifier}' does not define the scope '{scopeValue}'."
                    );
                }
            }

            string action;
            if (target is null)
            {
                action = TenantPromotionActions.Created;
            }
            else if (
                new HashSet<string>(grant.Scopes, StringComparer.Ordinal).SetEquals(target.Scopes)
            )
            {
                action = TenantPromotionActions.Unchanged;
            }
            else
            {
                action = TenantPromotionActions.Updated;
            }

            promotions.Add(
                new ClientGrantPromotion
                {
                    ClientId = clientId,
                    Source = grant,
                    Target = target,
                    TargetResourceServerId = targetResourceServerId,
                    Action = action,
                    Errors = errors,
                }
            );
        }

        return promotions;
    }

    // The document holds no grant of a machine client, which belongs to its owning node (ADR-1466). The grants of a
    // client the host manages stay out.
    private List<TenantConfigurationClientGrant> GetSourceGrants(TenantPromotionContext context) =>
        [
            .. context.Source.ClientGrants.Where(grant =>
                !HostManagedClientChecker.IsHostManaged(
                    context.Source.SourceTenantId,
                    grant.ClientId
                )
                && !HostManagedClientChecker.IsHostManaged(
                    context.TargetTenant.TenantId,
                    grant.ClientId
                )
            ),
        ];
}
