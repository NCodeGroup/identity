// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Collections.Providers;
using NCode.Identity.Registration;
using NCode.Identity.Settings;
using NCode.Identity.SharedState;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Teams;

/// <summary>
/// Provides extension methods to configure services for teams.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Configures the team services.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddTeamServices()
        {
            builder.ServiceCollection.TryAddScoped<ITeamProvisioner, DefaultTeamProvisioner>();

            builder.ServiceCollection.TryAddSingleton<
                IOpenIdTeamFactory,
                DefaultOpenIdTeamFactory
            >();
            builder.ServiceCollection.TryAddSingleton<IOpenIdTeamCache, DefaultOpenIdTeamCache>();
            builder.ServiceCollection.TryAddSingleton<
                IOpenIdTeamProvider,
                DefaultOpenIdTeamProvider
            >();

            // The settings that govern a team are contributed as descriptors.
            var teamSettingDescriptorDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                TeamSettingDescriptorDataSource
            >();
            builder.ServiceCollection.TryAddEnumerable(teamSettingDescriptorDataSource);

            // A team change evicts the team and its tenants from this replica's caches and announces the change, and the
            // announcement evicts them from every other replica's (ADR-1411).
            var teamChangedHandler = ServiceDescriptor.Singleton<
                ICommandHandler<TeamChangedCommand>,
                TeamChangedHandler
            >();
            builder.ServiceCollection.TryAddEnumerable(teamChangedHandler);
            var teamChangeBroadcastHandler = ServiceDescriptor.Singleton<
                IReplicaBroadcastHandler,
                TeamChangeBroadcastHandler
            >();
            builder.ServiceCollection.TryAddEnumerable(teamChangeBroadcastHandler);

            // One replica reconciles each change to a team's effective settings in the background (ADR-1412).
            builder.ServiceCollection.TryAddSingleton<TeamSettingsReconciliationQueue>();
            builder.ServiceCollection.TryAddSingleton<
                ITeamSettingsReconciliationTrigger,
                DefaultTeamSettingsReconciliationTrigger
            >();
            builder.ServiceCollection.TryAddSingleton<
                ITeamSettingsReconciler,
                DefaultTeamSettingsReconciler
            >();
            builder.ServiceCollection.AddHostedService<TeamSettingsReconciliationService>();

            // A team's subtree is walked one page of child teams per level (ADR-1426).
            builder.ServiceCollection.TryAddSingleton<
                ITeamDescendantWalker,
                DefaultTeamDescendantWalker
            >();

            return builder;
        }
    }
}
