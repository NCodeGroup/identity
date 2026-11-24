// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Events;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Provides the default implementation of <see cref="IManagementKeyRevoker"/>. In one unit of work it removes the
/// key from the control-plane tenant, activates the key's pending successor of the same use at once or, when none
/// exists, generates a successor that is active at once, and records the revocation, so the outbox that the
/// persistence layer writes publishes the management keys document in the same commit. Once the unit of work has
/// committed, the revocation is audited, the tenant is announced so every replica reloads its key set, and a revoked
/// remote key is destroyed in its key management service.
/// </summary>
// See ADR-1536 and ADR-1351.
internal sealed class DefaultManagementKeyRevoker(
    IOptions<OpenIdOptions> optionsAccessor,
    ITenantResolver tenantResolver,
    ITenantKeyPolicy tenantKeyPolicy,
    ITenantKeyRollover rollover,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IStoreManagerFactory storeManagerFactory,
    IEventPublisher eventPublisher,
    IRemoteSecretDestroyer remoteSecretDestroyer,
    IMediator mediator,
    TimeProvider timeProvider,
    ILogger<DefaultManagementKeyRevoker> logger
) : IManagementKeyRevoker
{
    private IOptions<OpenIdOptions> OptionsAccessor { get; } = optionsAccessor;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private ITenantKeyPolicy TenantKeyPolicy { get; } = tenantKeyPolicy;
    private ITenantKeyRollover Rollover { get; } = rollover;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IEventPublisher EventPublisher { get; } = eventPublisher;
    private IRemoteSecretDestroyer RemoteSecretDestroyer { get; } = remoteSecretDestroyer;
    private IMediator Mediator { get; } = mediator;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DefaultManagementKeyRevoker> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<ManagementKeyRevocationResult> RevokeAsync(
        string keyId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(keyId);

        var tenantId = TenantResolver.GetControlPlaneTenantId();
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenantStore = storeManager.GetStore<ITenantStore>();
        var tenant = await tenantStore.GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
            return ManagementKeyRevocationResult.NotFound;

        var held = tenant.Secrets.Value;
        var revoked = held.FirstOrDefault(secret =>
            string.Equals(secret.KeyId ?? secret.SecretId, keyId, StringComparison.Ordinal)
        );
        if (revoked is null)
            return ManagementKeyRevocationResult.NotFound;

        var now = TimeProvider.GetUtcNow();
        var remaining = held.Where(secret => secret.SecretId != revoked.SecretId).ToList();
        await tenantStore.RemoveSecretAsync(tenantId, revoked.SecretId, cancellationToken);

        var plan = new TenantKeyRolloverPlan { Now = now };
        var successor = FindPendingSuccessor(remaining, revoked, now);
        if (successor is not null)
        {
            var activation = new TenantKeyActivation { Secret = successor, ActivatesWhen = now };
            plan.Activations.Add(activation);
        }
        else
        {
            var generation = await CreateGenerationAsync(tenant, revoked, now, cancellationToken);
            plan.Generations.Add(generation);
        }

        var result = await Rollover.ApplyAsync(
            tenantStore,
            tenantId,
            remaining,
            plan,
            cancellationToken
        );

        var replacementKeyId = GetReplacementKeyId(successor, remaining, result);

        var replicatedDocumentStore = storeManager.GetStore<IReplicatedDocumentStore>();
        var revokedKey = new PersistedRevokedManagementKey
        {
            KeyId = keyId,
            RevokedWhen = now,
            Source = PersistedRevokedManagementKey.ControlPlaneSource,
        };
        await replicatedDocumentStore.AddRevokedKeyAsync(revokedKey, cancellationToken);

        await storeManager.SaveChangesAsync(cancellationToken);

        var revokedEvent = CreateAuditEvent(tenantId, revoked, now);
        var auditEvents = new List<TenantKeyAuditEvent> { revokedEvent };
        auditEvents.AddRange(result.AuditEvents);
        await RunAfterCommitAsync(tenantId, auditEvents, revoked);

        return ManagementKeyRevocationResult.Revoked(replacementKeyId);
    }

    // A published key of the same use that activates later is already in every cell's replica, so activating it
    // pauses no management request.
    private static PersistedSecret? FindPendingSuccessor(
        IReadOnlyList<PersistedSecret> remaining,
        PersistedSecret revoked,
        DateTimeOffset now
    ) =>
        remaining
            .Where(secret =>
                secret.ActivatesWhen > now
                && secret.ExpiresWhen > now
                && string.Equals(secret.Use, revoked.Use, StringComparison.Ordinal)
            )
            .OrderBy(secret => secret.ActivatesWhen)
            .FirstOrDefault();

    private static string GetReplacementKeyId(
        PersistedSecret? successor,
        IReadOnlyList<PersistedSecret> remaining,
        TenantKeyRolloverResult result
    )
    {
        if (successor is not null)
            return successor.KeyId ?? successor.SecretId;

        var remainingIds = remaining
            .Select(secret => secret.SecretId)
            .ToHashSet(StringComparer.Ordinal);
        var generated = result.Secrets.First(secret => !remainingIds.Contains(secret.SecretId));
        return generated.KeyId ?? generated.SecretId;
    }

    private async ValueTask<TenantKeyGeneration> CreateGenerationAsync(
        PersistedTenant tenant,
        PersistedSecret revoked,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var options = OptionsAccessor.Value.Tenant;
        var resolution = TenantKeyPolicy.ResolveSpecifications(options.KeyProvisioning);
        if (resolution.Errors.Count > 0)
        {
            var errors = string.Join(" ", resolution.Errors);
            throw new InvalidOperationException(
                "The tenant key provisioning options cannot be honored: " + errors
            );
        }

        var teamSettings = await EffectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        var settings = EffectiveSettingsResolver.Resolve(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenant.Settings.Value
        );

        var specifications = TenantKeyPolicy.ForTenant(resolution.Specifications, settings);
        var specification = specifications.FirstOrDefault(candidate =>
            string.Equals(candidate.Algorithm, revoked.Algorithm, StringComparison.Ordinal)
        );
        if (string.IsNullOrEmpty(specification.Algorithm))
        {
            throw new InvalidOperationException(
                $"The key provisioning names no key for the algorithm of the revoked key '{revoked.SecretId}', so no successor can be generated."
            );
        }

        var lifetimes = TenantKeyLifetimes.Read(
            settings,
            options.KeyRollover.TokenLifetimeSettings
        );
        var retiresWhen = now + lifetimes.Lifetime;
        return new TenantKeyGeneration
        {
            Specification = specification,
            ActivatesWhen = now,
            RetiresWhen = retiresWhen,
            ExpiresWhen = retiresWhen + lifetimes.RetirementGrace,
            Reason = TenantKeyChangeReasons.Compromised,
        };
    }

    private static TenantKeyAuditEvent CreateAuditEvent(
        string tenantId,
        PersistedSecret secret,
        DateTimeOffset now
    ) =>
        new()
        {
            EventId = Guid.CreateVersion7(now),
            EventTime = now,
            EventSource = AuditEventSources.OpenId,
            TenantId = tenantId,
            Actor = AuditActor.System,
            Outcome = AuditOutcome.Success,
            Change = TenantKeyChanges.Revoked,
            Reason = TenantKeyChangeReasons.Compromised,
            SecretId = secret.SecretId,
            KeyId = secret.KeyId ?? secret.SecretId,
            SecretType = secret.SecretType,
            Algorithm = secret.Algorithm,
            Use = secret.Use,
            ActivatesWhen = secret.ActivatesWhen,
            RetiresWhen = secret.RetiresWhen,
            ExpiresWhen = secret.ExpiresWhen,
        };

    // The change is committed, so the audit events are published and the tenant is announced on their own bounded
    // tokens, and the announcement follows whatever the publishing did, because every replica must reload the key
    // set. A revoked remote key is destroyed last, in a finally of its own. See ADR-1351 and ADR-1471.
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The work follows a committed change and runs on its own bounded token, so no caller can pass a token that drops it (ADR-1351, ADR-1471)."
    )]
    private async ValueTask RunAfterCommitAsync(
        string tenantId,
        IReadOnlyList<TenantKeyAuditEvent> auditEvents,
        PersistedSecret revoked
    )
    {
        try
        {
            try
            {
                await PostCommitWork.RunAsync(
                    TimeProvider,
                    PublishAuditEventsAsync,
                    (tenantId, auditEvents)
                );
            }
            finally
            {
                await PostCommitWork.RunAsync(
                    TimeProvider,
                    static (state, token) =>
                    {
                        var command = new TenantChangedCommand(
                            state.TenantId,
                            SettingsChanged: false
                        );
                        return state.Mediator.SendAsync(command, token);
                    },
                    (Mediator, TenantId: tenantId)
                );
            }
        }
        finally
        {
            await RemoteSecretDestroyer.DestroyAsync([revoked]);
        }
    }

    private async ValueTask PublishAuditEventsAsync(
        (string TenantId, IReadOnlyList<TenantKeyAuditEvent> AuditEvents) state,
        CancellationToken cancellationToken
    )
    {
        try
        {
            foreach (var auditEvent in state.AuditEvents)
            {
                await EventPublisher.PublishAsync(auditEvent, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            Logger.TenantKeyAuditPublishFailed(state.TenantId, exception);
        }
    }
}
