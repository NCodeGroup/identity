// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Events;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Settings;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Seeds every tenant with the keys its settings use, and rolls them over, whenever the tenant is materialized: it
/// applies the rollover plan the tenant's keys and settings call for, so a tenant that is served gets its first key,
/// the successor of a key that nears its retirement, and the retirement and removal of the keys it does not need,
/// without waiting for the background pass. Keys are generated through the normal secret-persistence path, so the
/// runtime, the JSON Web Key Set, and the management API all read the same keys. The audit event of each change is
/// published once the seed's unit of work has committed, so a seed that loses its commit records nothing. A cell never
/// changes the keys of a tenant whose key custody is fenced by a move to or from another cell, because removing one
/// destroys a remote key that cell uses.
/// </summary>
/// <remarks>
/// A key the handler cannot read still counts as held: an undecryptable key signals a lost master key or root key,
/// which must fail the tenant rather than be papered over with a new key. The tenant's master key is loaded only
/// when a key has to be generated.
/// </remarks>
// See ADR-1166 (every tenant is seeded through one pipeline), ADR-1169 (every tenant is provisioned with persisted
// keys) and ADR-1404 (the keys roll over).
internal class TenantKeySeedHandler(
    IOptions<OpenIdOptions> optionsAccessor,
    ITenantKeyPolicy tenantKeyPolicy,
    ITenantKeyRolloverPlanner planner,
    ITenantKeyRollover rollover,
    ITenantKeyCustodyGuard custodyGuard,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IEventPublisher eventPublisher,
    IRemoteSecretDestroyer remoteSecretDestroyer,
    TimeProvider timeProvider
) : ICommandHandler<SeedTenantCommand>, ISupportMediatorPriority
{
    private OpenIdTenantKeyProvisioningOptions Options { get; } =
        optionsAccessor.Value.Tenant.KeyProvisioning;

    private ITenantKeyPolicy TenantKeyPolicy { get; } = tenantKeyPolicy;
    private ITenantKeyRolloverPlanner Planner { get; } = planner;
    private ITenantKeyRollover Rollover { get; } = rollover;
    private ITenantKeyCustodyGuard CustodyGuard { get; } = custodyGuard;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;
    private IEventPublisher EventPublisher { get; } = eventPublisher;
    private IRemoteSecretDestroyer RemoteSecretDestroyer { get; } = remoteSecretDestroyer;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.Low;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        SeedTenantCommand command,
        CancellationToken cancellationToken
    )
    {
        if (!Options.Enabled)
        {
            return;
        }

        var resolution = TenantKeyPolicy.ResolveSpecifications(Options);
        if (resolution.Errors.Count > 0)
        {
            var errors = string.Join(" ", resolution.Errors);
            throw new InvalidOperationException(
                "The tenant key provisioning options cannot be honored: " + errors
            );
        }

        var context = command.Context;
        var tenant = context.Tenant;
        var held = tenant.Secrets.Value;
        var settings = await GetEffectiveSettingsAsync(tenant, cancellationToken);

        var now = TimeProvider.GetUtcNow();
        var plan = Planner.Plan(resolution.Specifications, held, settings, now);
        if (plan.IsEmpty)
        {
            return;
        }

        // A cell never changes the keys of a tenant whose custody is fenced by a move, because removing one
        // destroys a remote key that cell uses (ADR-1514).
        var mayChangeKeys = await CustodyGuard.MayChangeKeysAsync(
            tenant.TenantId,
            cancellationToken
        );
        if (!mayChangeKeys)
        {
            return;
        }

        var tenantStore = context.StoreManager.GetStore<ITenantStore>();
        var result = await Rollover.ApplyAsync(
            tenantStore,
            tenant.TenantId,
            held,
            plan,
            cancellationToken
        );

        // The contributors that follow read the tenant's secrets from this instance, before the unit of work commits,
        // so it carries the keys as the plan left them.
        tenant.Secrets = new PersistedTenantSecrets
        {
            TenantId = tenant.Secrets.TenantId,
            ConcurrencyToken = tenant.Secrets.ConcurrencyToken,
            Value = result.Secrets,
        };

        // A change is audited once it is committed, never before: a seed that loses its commit runs again from what
        // the winner wrote and records only what it then changes.
        context.RunAfterCommit(token => PublishAsync(result.AuditEvents, token));

        // A key the seed removed is destroyed in its key management service only once the removal is committed, as its
        // own step, so a publish that fails does not leave the key behind. See ADR-1471.
        context.RunAfterCommit(_ => RemoteSecretDestroyer.DestroyAsync(result.RemovedSecrets));
    }

    private async ValueTask PublishAsync(
        IReadOnlyList<TenantKeyAuditEvent> auditEvents,
        CancellationToken cancellationToken
    )
    {
        foreach (var auditEvent in auditEvents)
        {
            await EventPublisher.PublishAsync(auditEvent, cancellationToken);
        }
    }

    // The tenant's settings materialize only after the seed, so the seed resolves the tenant's persisted document onto
    // its team's committed settings through the resolver, as the materialized tenant will (see ADR-1204).
    private async ValueTask<IReadOnlySettingCollection> GetEffectiveSettingsAsync(
        PersistedTenant tenant,
        CancellationToken cancellationToken
    )
    {
        var teamSettings = await EffectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        return EffectiveSettingsResolver.Resolve(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenant.Settings.Value
        );
    }
}
