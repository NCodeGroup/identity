// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Clients;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides an implementation of <see cref="ITenantPromotionContributor"/> that promotes the source tenant's clients
/// that the host does not manage. A client is matched by its client identifier and keeps it in the target tenant, and
/// the resource server a client acts for is the one the promotion maps for it.
/// </summary>
// See ADR-1425.
internal class ClientPromotionContributor(
    IPromotedSettingsComposer promotedSettingsComposer,
    ISettingSerializer settingSerializer,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IClientCoherenceValidator clientCoherenceValidator,
    ITenantClientCoherenceChecker tenantClientCoherenceChecker,
    IClientKeySourceChecker clientKeySourceChecker,
    IHostManagedClientChecker hostManagedClientChecker,
    IClientJwksUriChecker clientJwksUriChecker
) : ITenantPromotionContributor
{
    private IPromotedSettingsComposer PromotedSettingsComposer { get; } = promotedSettingsComposer;

    private ISettingSerializer SettingSerializer { get; } = settingSerializer;

    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    private IClientCoherenceValidator ClientCoherenceValidator { get; } = clientCoherenceValidator;

    private ITenantClientCoherenceChecker TenantClientCoherenceChecker { get; } =
        tenantClientCoherenceChecker;

    private IClientKeySourceChecker ClientKeySourceChecker { get; } = clientKeySourceChecker;

    private IHostManagedClientChecker HostManagedClientChecker { get; } = hostManagedClientChecker;

    private IClientJwksUriChecker ClientJwksUriChecker { get; } = clientJwksUriChecker;

    /// <inheritdoc />
    public int Order => TenantPromotionOrders.Clients;

    /// <inheritdoc />
    public async ValueTask PlanAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var computation = await ComputeAsync(context, cancellationToken);

        foreach (var clientId in computation.HostManagedSourceClientIds)
        {
            var hostManagedNote = new TenantPromotionNoteResource
            {
                ResourceType = ManagementResourceTypes.Client,
                ResourceId = clientId,
                Code = TenantPromotionNoteCodes.HostManaged,
                Detail =
                    "The host configures this client in the source tenant, so the promotion leaves it and its grants out.",
            };
            context.AddNote(hostManagedNote);
        }

        foreach (var promotion in computation.Promotions)
        {
            var clientId = promotion.Source.ClientId;

            foreach (var detail in promotion.Errors)
            {
                var errorResource = new TenantPromotionErrorResource
                {
                    ResourceType = ManagementResourceTypes.Client,
                    ResourceId = clientId,
                    Detail = detail,
                };
                context.AddError(errorResource);
            }

            var changeResource = new TenantPromotionChangeResource
            {
                ResourceType = ManagementResourceTypes.Client,
                ResourceId = clientId,
                Action = promotion.Action,
            };
            context.AddChange(changeResource);

            foreach (var settingName in promotion.Settings.UnsetSettingNames)
            {
                var unsetNote = new TenantPromotionNoteResource
                {
                    ResourceType = ManagementResourceTypes.Client,
                    ResourceId = clientId,
                    Code = TenantPromotionNoteCodes.EnvironmentSettingUnset,
                    Detail =
                        $"The source client sets '{settingName}', which belongs to its environment, and the promoted settings do not.",
                    SettingName = settingName,
                };
                context.AddNote(unsetNote);
            }

            if (promotion.IsCredentialRequired)
            {
                var credentialNote = new TenantPromotionNoteResource
                {
                    ResourceType = ManagementResourceTypes.Client,
                    ResourceId = clientId,
                    Code = TenantPromotionNoteCodes.CredentialRequired,
                    Detail =
                        "The client authenticates with a credential, and the target tenant holds none for it, so the target needs one before the client can authenticate.",
                };
                context.AddNote(credentialNote);
            }

            if (
                promotion.Action == TenantPromotionActions.Updated
                && promotion.IsSettingsChanged
                && promotion.Target is { } target
            )
            {
                var changedLimits = SettingSerializer.GetChangedLimits(
                    target.Settings.Value,
                    promotion.Settings.Document
                );
                context.AddChangedLimits(context.TargetTenantNode, changedLimits);
            }
        }

        await AddIntroducedViolationsAsync(context, computation, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var computation = await ComputeAsync(context, cancellationToken);

        foreach (var promotion in computation.Promotions)
        {
            if (promotion.Action == TenantPromotionActions.Unchanged)
            {
                continue;
            }

            using var scope = context.BeginTargetScope();

            if (promotion.Target is null)
            {
                await CreateAsync(context, promotion, cancellationToken);
            }
            else
            {
                await UpdateAsync(context, promotion, promotion.Target, cancellationToken);
            }
        }
    }

    private static async ValueTask CreateAsync(
        TenantPromotionContext context,
        ClientPromotion promotion,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IClientStore>();
        var tenantId = context.TargetTenant.TenantId;
        var source = promotion.Source;

        var client = new PersistedClient
        {
            TenantId = tenantId,
            ClientId = source.ClientId,
            ConcurrencyToken = string.Empty,
            IsDisabled = source.IsDisabled,
            ResourceServerId = promotion.TargetResourceServerId,
            Settings = new PersistedClientSettings
            {
                TenantId = tenantId,
                ClientId = source.ClientId,
                ConcurrencyToken = string.Empty,
                Value = promotion.Settings.Document,
            },
            Secrets = new PersistedClientSecrets
            {
                TenantId = tenantId,
                ClientId = source.ClientId,
                ConcurrencyToken = string.Empty,
                Value = [],
            },
        };

        await store.AddAsync(client, cancellationToken);

        AddAuditEvent(context, client, ResourceChangeTypes.Created);
    }

    private static async ValueTask UpdateAsync(
        TenantPromotionContext context,
        ClientPromotion promotion,
        PersistedClient target,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IClientStore>();

        if (target.IsDisabled != promotion.Source.IsDisabled)
        {
            target.IsDisabled = promotion.Source.IsDisabled;
            await store.UpdateAsync(target, cancellationToken);
        }

        if (promotion.IsSettingsChanged)
        {
            target.Settings.Value = promotion.Settings.Document;
            await store.UpdateSettingsAsync(target.Settings, cancellationToken);
        }

        AddAuditEvent(context, target, ResourceChangeTypes.Updated);
    }

    private static void AddAuditEvent(
        TenantPromotionContext context,
        PersistedClient client,
        string changeType
    )
    {
        var resource = ToResource(client);
        var resourceValues = JsonSerializer.SerializeToElement(resource, JsonSerializerOptions.Web);
        var auditEvent = new ClientChangedAuditEvent
        {
            EventSource = AuditEventSources.Management,
            TenantId = context.TargetTenant.TenantId,
            ResourceId = client.ClientId,
            ChangeType = changeType,
            ResourceValues = resourceValues,
            Outcome = AuditOutcome.Success,
        };
        context.AddAuditEvent(auditEvent);
    }

    private static ClientResource ToResource(PersistedClient client) =>
        new()
        {
            TenantId = client.TenantId,
            ClientId = client.ClientId,
            ConcurrencyToken = client.ConcurrencyToken,
            IsDisabled = client.IsDisabled,
            IsHostManaged = false,
            PrincipalId = client.PrincipalId,
            ResourceServerId = client.ResourceServerId,
        };

    private static bool IsCredentialRequired(JsonElement settings, PersistedClient? target)
    {
        string? method = null;
        if (
            settings.ValueKind == JsonValueKind.Object
            && settings.TryGetPropertyValue<string>(
                OpenIdSettingNames.TokenEndpointAuthMethod,
                out var value
            )
        )
        {
            method = value;
        }

        return !string.Equals(
                method,
                OpenIdProtocol.ClientAuthenticationMethods.None,
                StringComparison.Ordinal
            ) && (target is null || target.Secrets.Value.Count == 0);
    }

    private async ValueTask AddIntroducedViolationsAsync(
        TenantPromotionContext context,
        ClientPromotionComputation computation,
        CancellationToken cancellationToken
    )
    {
        var targetTenant = context.TargetTenant;
        if (
            computation.TeamSettings is not { } teamSettings
            || computation.NewTenantSettings is not { } newTenantSettings
            || JsonElement.DeepEquals(context.TargetTenantSettings, targetTenant.Settings.Value)
        )
        {
            return;
        }

        IReadOnlySettingCollection currentTenantSettings;
        try
        {
            currentTenantSettings = EffectiveSettingsResolver.Resolve(
                teamSettings,
                OpenIdSettingScopes.Tenant,
                targetTenant.Settings.Value
            );
        }
        catch (JsonException)
        {
            // The stored document cannot be honored, so a violation cannot be told from one that predates the change.
            return;
        }

        IReadOnlyList<ClientCoherenceFinding> findings;
        using (context.BeginTargetScope())
        {
            findings = await TenantClientCoherenceChecker.FindIntroducedViolationsAsync(
                context.StoreManager,
                targetTenant.TenantId,
                currentTenantSettings,
                newTenantSettings,
                cancellationToken
            );
        }

        var promotedClientIds = computation
            .Promotions.Select(x => x.Source.ClientId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            if (promotedClientIds.Contains(finding.ClientId))
            {
                continue;
            }

            var violationError = new TenantPromotionErrorResource
            {
                ResourceType = ManagementResourceTypes.Client,
                ResourceId = finding.ClientId,
                Detail =
                    $"The promoted tenant settings would make the client '{finding.ClientId}' incoherent: {finding.Violation.Description}",
            };
            context.AddError(violationError);
        }
    }

    private async ValueTask<ClientPromotionComputation> ComputeAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IClientStore>();
        var computation = new ClientPromotionComputation();

        var teamSettings = await EffectiveSettingsResolver.ResolveTeamAsync(
            context.TargetTenant.TeamId,
            cancellationToken
        );
        computation.TeamSettings = teamSettings;

        string? newTenantSettingsError = null;
        try
        {
            computation.NewTenantSettings = EffectiveSettingsResolver.Resolve(
                teamSettings,
                OpenIdSettingScopes.Tenant,
                context.TargetTenantSettings
            );
        }
        catch (JsonException exception)
        {
            newTenantSettingsError =
                $"The promoted tenant settings cannot be resolved, so the client's coherence cannot be checked: {exception.Message}";
        }

        var sources = context.Source.Clients;

        foreach (var source in sources)
        {
            // The document holds no machine client, which belongs to its owning node, not to the tenant's
            // configuration. See ADR-1466.
            if (
                HostManagedClientChecker.IsHostManaged(
                    context.Source.SourceTenantId,
                    source.ClientId
                )
            )
            {
                computation.HostManagedSourceClientIds.Add(source.ClientId);
                continue;
            }

            using var scope = context.BeginTargetScope();

            var errors = new List<string>();
            if (newTenantSettingsError is not null)
            {
                errors.Add(newTenantSettingsError);
            }

            string? targetResourceServerId = null;
            var isResourceServerResolved = true;
            if (!string.IsNullOrEmpty(source.ResourceServerId))
            {
                if (
                    context.TryGetTargetIdentifier(
                        ManagementResourceTypes.ResourceServer,
                        source.ResourceServerId,
                        out var mappedId
                    )
                )
                {
                    targetResourceServerId = mappedId;
                }
                else
                {
                    isResourceServerResolved = false;
                    errors.Add(
                        $"The resource server '{source.ResourceServerId}' the client acts for is neither promoted nor a system resource server of the target tenant."
                    );
                }
            }

            var target = await store.GetOrDefaultAsync(source.ClientId, cancellationToken);
            if (target is not null)
            {
                if (
                    HostManagedClientChecker.IsHostManaged(
                        context.TargetTenant.TenantId,
                        target.ClientId
                    )
                )
                {
                    errors.Add(
                        "The host configures the client in the target tenant, so the promotion does not change it."
                    );
                }

                if (target.OwningNodeType is not null)
                {
                    errors.Add(
                        "The client in the target tenant is a machine client of the platform or a team, so the promotion does not change it."
                    );
                }

                if (isResourceServerResolved)
                {
                    var currentResourceServerId = string.IsNullOrEmpty(target.ResourceServerId)
                        ? null
                        : target.ResourceServerId;
                    if (
                        !string.Equals(
                            currentResourceServerId,
                            targetResourceServerId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        errors.Add(
                            "The client in the target tenant acts for another resource server than the promoted one."
                        );
                    }
                }
            }

            var environmentSettings = context.GetEnvironmentSettingsOrDefault(
                ManagementResourceTypes.Client,
                source.ClientId
            );
            var promoted = PromotedSettingsComposer.Compose(
                source.Settings,
                target?.Settings.Value,
                environmentSettings
            );

            foreach (var settingName in promoted.RefusedSettingNames)
            {
                errors.Add(
                    $"The setting '{settingName}' is not bound to the environment, so the request cannot supply it."
                );
            }

            var settingErrors = SettingSerializer.ValidateSettings(
                promoted.Document,
                OpenIdSettingScopes.Client,
                context.JsonSerializerOptions
            );
            foreach (var settingError in settingErrors)
            {
                errors.Add(settingError.Message);
            }

            if (computation.NewTenantSettings is { } newTenantSettings)
            {
                try
                {
                    var violations = ClientCoherenceValidator.Validate(
                        newTenantSettings,
                        promoted.Document
                    );
                    foreach (var violation in violations)
                    {
                        errors.Add(violation.Description);
                    }
                }
                catch (JsonException exception)
                {
                    errors.Add(exception.Message);
                }
            }

            if (
                target is not null
                && ClientKeySourceChecker.HasJwksUri(promoted.Document)
                && ClientKeySourceChecker.HasPublicKeys(target.Secrets.Value)
            )
            {
                errors.Add(
                    "A client's public keys come from one source: the keys registered for it or the keys it publishes at its 'jwks_uri', never both."
                );
            }

            if (
                await ClientJwksUriChecker.GetRefusalAsync(
                    promoted.Document,
                    target?.Settings.Value,
                    cancellationToken
                ) is
                { } refusal
            )
            {
                errors.Add(refusal.Message);
            }

            var isSettingsChanged =
                target is not null
                && !JsonElement.DeepEquals(promoted.Document, target.Settings.Value);

            string action;
            if (target is null)
            {
                action = TenantPromotionActions.Created;
            }
            else if (target.IsDisabled != source.IsDisabled || isSettingsChanged)
            {
                action = TenantPromotionActions.Updated;
            }
            else
            {
                action = TenantPromotionActions.Unchanged;
            }

            var isCredentialRequired = IsCredentialRequired(promoted.Document, target);
            var clientPromotion = new ClientPromotion
            {
                Source = source,
                Target = target,
                TargetResourceServerId = targetResourceServerId,
                Settings = promoted,
                Action = action,
                Errors = errors,
                IsSettingsChanged = isSettingsChanged,
                IsCredentialRequired = isCredentialRequired,
            };
            computation.Promotions.Add(clientPromotion);
        }

        return computation;
    }
}
