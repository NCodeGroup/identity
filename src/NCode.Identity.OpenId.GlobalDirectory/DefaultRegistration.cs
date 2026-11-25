// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        /// <see cref="IPlatformDirectory"/> and <see cref="ICellDirectory"/> when the <c>Directory:Url</c> setting is
        /// set, and registers nothing otherwise, so a cell that runs on its own keeps the local adapters. The
        /// <c>Directory</c> section binds <see cref="DirectoryClientOptions"/> strictly and is validated when the host
        /// starts. Each adapter calls the <c>directory/v1</c> API with a client-credentials token that the directory's
        /// own token endpoint issues to the cell's machine client, over the client library's bounded HTTP client. A
        /// start check also refuses a client identifier other than <c>cell-</c> followed by the cell's name, a key ring
        /// the client library lacks, a blank management URL and, for a cell that is not the control plane, a
        /// <c>Cell:ControlPlane:Issuer</c> that is not an allowed endpoint. A call that cannot reach the directory
        /// throws <see cref="DirectoryUnavailableException"/>. The same registration adds a hosted service that
        /// registers the cell with the directory when the host starts, retrying while the directory is unavailable, and
        /// a hosted service that follows the directory's change feed: every
        /// <see cref="DirectoryClientOptions.PollInterval"/> one replica of a cell that is not the control plane
        /// mirrors the published team and platform documents into the cell, and a hosted service of the control-plane
        /// cell publishes its outbox of replicated documents to the directory every
        /// <see cref="DirectoryClientOptions.PublishInterval"/> and when a commit wakes it. A cell that is not the
        /// control plane (<see cref="CellOptions.IsControlPlane"/> is <c>false</c>) uses the
        /// <see cref="IManagementTokenValidator"/> that validates management tokens against the management keys that
        /// its replica holds and the <see cref="IControlPlaneAuthority"/> that answers the roles of a caller from the
        /// platform and team authority documents that its replica holds, and an
        /// <see cref="IControlPlanePrincipalDirectory"/> that describes no principal. This registration adds all three
        /// as keyed singletons under <see cref="ControlPlaneServiceKeys.Remote"/> on every cell and reads no
        /// <c>Cell</c> setting; the management library resolves them for a cell that is not the control plane and
        /// resolves its local adapters for the control-plane cell.
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
            var optionsValidator = ServiceDescriptor.Singleton<
                IValidateOptions<DirectoryClientOptions>,
                DirectoryClientOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(optionsValidator);

            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<ICellTokenSource, DefaultCellTokenSource>();
            serviceCollection.TryAddSingleton<DirectoryHttpClient>();

            // The local adapters are registered with TryAdd, so Replace makes the remote adapters win whichever
            // registration runs first.
            var tenantDirectory = ServiceDescriptor.Singleton<
                ITenantDirectory,
                RemoteTenantDirectory
            >();
            serviceCollection.Replace(tenantDirectory);
            var teamDirectory = ServiceDescriptor.Singleton<ITeamDirectory, RemoteTeamDirectory>();
            serviceCollection.Replace(teamDirectory);
            var platformDirectory = ServiceDescriptor.Singleton<
                IPlatformDirectory,
                RemotePlatformDirectory
            >();
            serviceCollection.Replace(platformDirectory);
            var cellDirectory = ServiceDescriptor.Singleton<ICellDirectory, RemoteCellDirectory>();
            serviceCollection.Replace(cellDirectory);
            var replicatedDirectory = ServiceDescriptor.Singleton<
                IReplicatedDocumentDirectory,
                RemoteReplicatedDocumentDirectory
            >();
            serviceCollection.Replace(replicatedDirectory);
            var platformTopology = ServiceDescriptor.Singleton<
                IPlatformTopology,
                DirectoryPlatformTopology
            >();
            serviceCollection.Replace(platformTopology);

            // Every cell registers the remote management adapters under the key that Management's registration looks
            // up. Management chooses between the local and the remote adapter from the cell's options at resolution, so
            // this registration never reads the Cell section: the control-plane cell keeps its local adapters, and
            // every other cell validates management tokens against the control plane's issuer.
            serviceCollection.TryAddKeyedSingleton<
                IManagementTokenValidator,
                ReplicatedManagementTokenValidator
            >(ControlPlaneServiceKeys.Remote);
            serviceCollection.TryAddKeyedSingleton<
                IControlPlaneAuthority,
                ReplicatedControlPlaneAuthority
            >(ControlPlaneServiceKeys.Remote);
            serviceCollection.TryAddKeyedSingleton<
                IControlPlanePrincipalDirectory,
                EmptyControlPlanePrincipalDirectory
            >(ControlPlaneServiceKeys.Remote);

            // The start check refuses an unusable control-plane issuer before the cell registers with the directory.
            var startupCheck = ServiceDescriptor.Singleton<
                IHostedService,
                DirectoryClientStartupCheck
            >();
            serviceCollection.TryAddEnumerable(startupCheck);

            // The cell announces itself to the directory when the host starts, then follows its change feed.
            var registrationService = ServiceDescriptor.Singleton<
                IHostedService,
                CellRegistrationService
            >();
            serviceCollection.TryAddEnumerable(registrationService);
            var changeFeedService = ServiceDescriptor.Singleton<
                IHostedService,
                DirectoryChangeFeedService
            >();
            serviceCollection.TryAddEnumerable(changeFeedService);
            var outboxPublisher = ServiceDescriptor.Singleton<
                IHostedService,
                DirectoryOutboxPublisher
            >();
            serviceCollection.TryAddEnumerable(outboxPublisher);

            // The replica's age is a gauge and a readiness check that never fails, so an operator sees a stale replica
            // while the cell keeps serving its last known documents.
            serviceCollection.TryAddSingleton<DirectoryReplicaHealth>();
            var replicaCheck = new HealthCheckRegistration(
                DirectoryReplicaHealth.HealthCheckName,
                static provider => provider.GetRequiredService<DirectoryReplicaHealth>(),
                failureStatus: null,
                tags: [HealthCheckTags.Readiness]
            );
            serviceCollection.AddHealthChecks().Add(replicaCheck);

            return builder;
        }
    }
}
