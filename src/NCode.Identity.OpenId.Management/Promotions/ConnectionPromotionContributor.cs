// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Connections;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides an implementation of <see cref="ITenantPromotionContributor"/> that promotes the source tenant's
/// connections. A connection is matched by its name, compared case-insensitively, and a created one gets an identifier
/// that the target tenant mints. A promotion names a connection by its name in its changes, errors and environment
/// settings, because the identifier of a created connection exists only once the promotion applies.
/// </summary>
// See ADR-1535.
internal class ConnectionPromotionContributor(
    IPromotedSettingsComposer promotedSettingsComposer,
    ISettingSerializer settingSerializer,
    IConnectionStrategyCatalog strategyCatalog,
    ICryptoService cryptoService
) : ITenantPromotionContributor
{
    private IPromotedSettingsComposer PromotedSettingsComposer { get; } = promotedSettingsComposer;

    private ISettingSerializer SettingSerializer { get; } = settingSerializer;

    private IConnectionStrategyCatalog StrategyCatalog { get; } = strategyCatalog;

    private ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public int Order => TenantPromotionOrders.Connections;

    /// <inheritdoc />
    public async ValueTask PlanAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var computation = await ComputeAsync(context, cancellationToken);

        foreach (var promotion in computation.Promotions)
        {
            var name = promotion.Source.Name;

            foreach (var detail in promotion.Errors)
            {
                var errorResource = new TenantPromotionErrorResource
                {
                    ResourceType = ManagementResourceTypes.Connection,
                    ResourceId = name,
                    Detail = detail,
                };
                context.AddError(errorResource);
            }

            var changeResource = new TenantPromotionChangeResource
            {
                ResourceType = ManagementResourceTypes.Connection,
                ResourceId = name,
                Action = promotion.Action,
            };
            context.AddChange(changeResource);

            foreach (var settingName in promotion.Settings.UnsetSettingNames)
            {
                var unsetNote = new TenantPromotionNoteResource
                {
                    ResourceType = ManagementResourceTypes.Connection,
                    ResourceId = name,
                    Code = TenantPromotionNoteCodes.EnvironmentSettingUnset,
                    Detail =
                        $"The source connection sets '{settingName}', which belongs to its environment, and the promoted settings do not.",
                    SettingName = settingName,
                };
                context.AddNote(unsetNote);
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

    private async ValueTask CreateAsync(
        TenantPromotionContext context,
        ConnectionPromotion promotion,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IConnectionStore>();
        var tenantId = context.TargetTenant.TenantId;
        var source = promotion.Source;
        var connectionId = CryptoService.GenerateResourceId();

        var connection = new PersistedConnection
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            ConcurrencyToken = string.Empty,
            Name = source.Name,
            Strategy = source.Strategy,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            Options = source.Options,
            Settings = new PersistedConnectionSettings
            {
                TenantId = tenantId,
                ConnectionId = connectionId,
                Value = promotion.Settings.Document,
            },
        };

        await store.AddAsync(connection, cancellationToken);

        AddAuditEvent(context, connection, ResourceChangeTypes.Created);
    }

    private static async ValueTask UpdateAsync(
        TenantPromotionContext context,
        ConnectionPromotion promotion,
        PersistedConnection target,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IConnectionStore>();
        var source = promotion.Source;

        if (IsConnectionChanged(source, target))
        {
            target.DisplayName = source.DisplayName;
            target.IsEnabled = source.IsEnabled;
            target.Options = source.Options;
            await store.UpdateAsync(target, cancellationToken);
        }

        if (promotion.IsSettingsChanged)
        {
            target.Settings.Value = promotion.Settings.Document;
            await store.UpdateSettingsAsync(target.Settings, cancellationToken);
        }

        AddAuditEvent(context, target, ResourceChangeTypes.Updated);
    }

    private static bool IsConnectionChanged(
        TenantConfigurationConnection source,
        PersistedConnection target
    ) =>
        !string.Equals(target.DisplayName, source.DisplayName, StringComparison.Ordinal)
        || target.IsEnabled != source.IsEnabled
        || !JsonElement.DeepEquals(target.Options, source.Options);

    private static void AddAuditEvent(
        TenantPromotionContext context,
        PersistedConnection connection,
        string changeType
    )
    {
        var resource = ToResource(connection);
        var resourceValues = JsonSerializer.SerializeToElement(resource, JsonSerializerOptions.Web);
        var auditEvent = new ConnectionChangedAuditEvent
        {
            EventSource = AuditEventSources.Management,
            TenantId = context.TargetTenant.TenantId,
            ResourceId = connection.ConnectionId,
            ChangeType = changeType,
            ResourceValues = resourceValues,
            Outcome = AuditOutcome.Success,
        };
        context.AddAuditEvent(auditEvent);
    }

    private static ConnectionResource ToResource(PersistedConnection connection) =>
        new()
        {
            TenantId = connection.TenantId,
            ConnectionId = connection.ConnectionId,
            ConcurrencyToken = connection.ConcurrencyToken,
            Name = connection.Name,
            DisplayName = connection.DisplayName,
            Strategy = connection.Strategy,
            IsEnabled = connection.IsEnabled,
            Options = connection.Options,
        };

    private async ValueTask<ConnectionPromotionComputation> ComputeAsync(
        TenantPromotionContext context,
        CancellationToken cancellationToken
    )
    {
        var store = context.StoreManager.GetStore<IConnectionStore>();
        var computation = new ConnectionPromotionComputation();

        foreach (var source in context.Source.Connections)
        {
            using var scope = context.BeginTargetScope();

            var target = await store.GetByNameOrDefaultAsync(source.Name, cancellationToken);

            var errors = new List<string>();
            AddStrategyErrors(source, target, errors);

            var environmentSettings = context.GetEnvironmentSettingsOrDefault(
                ManagementResourceTypes.Connection,
                source.Name
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
                OpenIdSettingScopes.Connection,
                context.JsonSerializerOptions
            );
            foreach (var settingError in settingErrors)
            {
                errors.Add(settingError.Message);
            }

            var isSettingsChanged =
                target is not null
                && !JsonElement.DeepEquals(promoted.Document, target.Settings.Value);

            string action;
            if (target is null)
            {
                action = TenantPromotionActions.Created;
            }
            else if (IsConnectionChanged(source, target) || isSettingsChanged)
            {
                action = TenantPromotionActions.Updated;
            }
            else
            {
                action = TenantPromotionActions.Unchanged;
            }

            var connectionPromotion = new ConnectionPromotion
            {
                Source = source,
                Target = target,
                Settings = promoted,
                Action = action,
                Errors = errors,
                IsSettingsChanged = isSettingsChanged,
            };
            computation.Promotions.Add(connectionPromotion);
        }

        return computation;
    }

    private void AddStrategyErrors(
        TenantConfigurationConnection source,
        PersistedConnection? target,
        List<string> errors
    )
    {
        if (
            target is not null
            && !string.Equals(target.Strategy, source.Strategy, StringComparison.Ordinal)
        )
        {
            errors.Add(
                $"The target tenant's connection '{source.Name}' uses the strategy '{target.Strategy}', not '{source.Strategy}', and a connection's strategy cannot change."
            );
            return;
        }

        if (!StrategyCatalog.TryGetStrategy(source.Strategy, out var strategy))
        {
            errors.Add(
                $"The strategy '{source.Strategy}' of the connection '{source.Name}' is not registered in the target."
            );
            return;
        }

        var optionsError = strategy.ValidateOptions(source.Options);
        if (optionsError is not null)
        {
            errors.Add(
                $"The options of the connection '{source.Name}' are invalid: {optionsError}"
            );
            return;
        }

        if (target is null)
        {
            return;
        }

        var changeError = strategy.ValidateOptionsChange(target.Options, source.Options);
        if (changeError is not null)
        {
            errors.Add(
                $"The options of the connection '{source.Name}' cannot change: {changeError}"
            );
        }
    }
}
