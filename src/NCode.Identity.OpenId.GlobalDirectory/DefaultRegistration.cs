// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides extension methods to register the remote adapters of the global directory.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder{T}"/> returned by <c>AddOpenIdClientLibrary</c>.</param>
    extension(IServiceBuilder<OpenIdClientLibrary> builder)
    {
        /// <summary>
        /// Registers the remote <see cref="ITenantDirectory"/>, <see cref="ITeamDirectory"/>,
        /// <see cref="IPlatformDirectory"/> and
        /// <see cref="ICellDirectory"/> when the <c>Directory:Url</c> setting is set, and registers nothing otherwise,
        /// so a cell that runs on its own keeps the local adapters. The <c>Directory</c> section binds
        /// <see cref="DirectoryClientOptions"/> strictly and is validated when the host starts. Each adapter calls the
        /// <c>directory/v1</c> API with a client-credentials token for the audience <c>urn:ncode:directory</c>, which
        /// the OpenID client library requests as the cell's machine client from the issuer named by
        /// <c>Cell:ControlPlane:Issuer</c>, over the client library's bounded HTTP client. A start check also refuses a
        /// <c>Cell:ControlPlane:Issuer</c> that is not an allowed endpoint. A call that cannot reach the
        /// directory throws <see cref="DirectoryUnavailableException"/>. The same registration adds a hosted service
        /// that registers the cell with the directory when the host starts, retrying while the directory is
        /// unavailable, and a hosted service that follows the directory's change feed: every
        /// <see cref="DirectoryClientOptions.PollInterval"/> one replica of a cell that is not the control plane
        /// mirrors the published team and platform documents into the cell. A cell that is not the control plane
        /// (<see cref="CellOptions.IsControlPlane"/> is <c>false</c>) uses the <see cref="IManagementTokenValidator"/>
        /// that validates management tokens against <c>Cell:ControlPlane:Issuer</c> and the
        /// <see cref="IControlPlaneAuthority"/> that asks the control-plane cell's <c>control-plane/v1/authority</c>
        /// endpoint for the roles of a caller, with a token for the audience <c>urn:ncode:control-plane</c> from the
        /// same <see cref="ICellTokenSource"/>. This registration adds both as keyed singletons under
        /// <see cref="ControlPlaneServiceKeys.Remote"/> on every cell and reads no <c>Cell</c>
        /// setting; the management library resolves them for a cell that is not the control plane and resolves its
        /// local adapters for the control-plane cell.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> holding the <c>Directory</c> section.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        [PublicAPI]
        public IServiceBuilder<OpenIdClientLibrary> AddOpenIdDirectoryClient(
            IConfiguration configuration
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection(DirectoryClientOptions.SectionName);
            // The directory's URL decides what gets registered, so it is read through the strictly bound options.
            var directory = new DirectoryClientOptions();
            section.BindStrictly(directory);
            if (string.IsNullOrEmpty(directory.Url))
                return builder;

            var serviceCollection = builder.ServiceCollection;

            serviceCollection
                .AddOptions<DirectoryClientOptions>()
                .Configure(options => section.BindStrictly(options))
                .ValidateOnStart();
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IValidateOptions<DirectoryClientOptions>,
                    DirectoryClientOptionsValidator
                >()
            );

            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<ICellTokenSource, DefaultCellTokenSource>();
            serviceCollection.TryAddSingleton<DirectoryHttpClient>();

            // The local adapters are registered with TryAdd, so Replace makes the remote adapters win whichever
            // registration runs first.
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<ITenantDirectory, RemoteTenantDirectory>()
            );
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<ITeamDirectory, RemoteTeamDirectory>()
            );
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<IPlatformDirectory, RemotePlatformDirectory>()
            );
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<ICellDirectory, RemoteCellDirectory>()
            );

            // Every cell registers the remote management adapters under the key that Management's registration looks
            // up. Management chooses between the local and the remote adapter from the cell's options at resolution, so
            // this registration never reads the Cell section: the control-plane cell keeps its local adapters, and
            // every other cell validates management tokens against the control plane's issuer.
            serviceCollection.TryAddKeyedSingleton<
                IManagementTokenValidator,
                RemoteManagementTokenValidator
            >(ControlPlaneServiceKeys.Remote);
            serviceCollection.TryAddKeyedSingleton<
                IControlPlaneAuthority,
                RemoteControlPlaneAuthority
            >(ControlPlaneServiceKeys.Remote);

            // The start check refuses an unusable control-plane issuer before the cell registers with the directory.
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, DirectoryClientStartupCheck>()
            );

            // The cell announces itself to the directory when the host starts, then follows its change feed.
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, CellRegistrationService>()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, DirectoryChangeFeedService>()
            );

            return builder;
        }
    }
}
