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
        /// directory throws <see cref="DirectoryUnavailableException"/>. The same registration adds a hosted service that
        /// registers the cell with the directory when the host starts, retrying while the directory is unavailable, and a
        /// hosted service that follows the directory's change feed: every <see cref="DirectoryClientOptions.PollInterval"/>
        /// one replica of a cell that is not the control plane mirrors the published team and platform documents into the
        /// cell.
        /// A cell that is not the control plane (<see cref="CellOptions.IsControlPlane"/> is <c>false</c>) also
        /// registers the <see cref="IManagementTokenValidator"/> that validates management tokens against
        /// <c>Cell:ControlPlane:Issuer</c> and the <see cref="IControlPlaneAuthority"/> that asks the control-plane cell's
        /// <c>control-plane/v1/authority</c> endpoint for the roles of a caller, with a token for the audience
        /// <c>urn:ncode:control-plane</c> from the same <see cref="ICellTokenSource"/>. Both replace the local adapters
        /// whichever registration runs first.
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
            if (string.IsNullOrEmpty(section[nameof(DirectoryClientOptions.Url)]))
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

            // The control-plane cell validates management tokens with its local adapter; every other cell validates
            // them against the control plane's issuer. Replace makes the remote adapter win over the local adapter's
            // TryAdd whichever registration runs first.
            if (
                !configuration.GetValue<bool>(
                    $"{CellOptions.SectionName}:{nameof(CellOptions.IsControlPlane)}"
                )
            )
            {
                serviceCollection.Replace(
                    ServiceDescriptor.Singleton<
                        IManagementTokenValidator,
                        RemoteManagementTokenValidator
                    >()
                );
                serviceCollection.Replace(
                    ServiceDescriptor.Singleton<
                        IControlPlaneAuthority,
                        RemoteControlPlaneAuthority
                    >()
                );
            }

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
