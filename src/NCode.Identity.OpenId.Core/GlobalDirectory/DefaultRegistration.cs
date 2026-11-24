// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides extension methods to configure services that apply the directory's change feed to the cell's rows.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">
    /// The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.
    /// </param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Configures the services that mirror the directory's team and platform documents into the cell's rows and
        /// record the highest sequence of the directory's change feed that the cell has applied.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddDirectoryMirrorServices()
        {
            builder.ServiceCollection.TryAddSingleton<IPlatformTopology, LocalPlatformTopology>();
            builder.ServiceCollection.TryAddSingleton<
                IPostCommitCommandSender,
                DefaultPostCommitCommandSender
            >();
            builder.ServiceCollection.TryAddSingleton<
                ITeamRootKeyMirror,
                DefaultTeamRootKeyMirror
            >();
            builder.ServiceCollection.TryAddSingleton<
                ITeamDocumentApplier,
                DefaultTeamDocumentApplier
            >();
            builder.ServiceCollection.TryAddSingleton<
                IPlatformDocumentApplier,
                DefaultPlatformDocumentApplier
            >();
            builder.ServiceCollection.TryAddSingleton<
                IReplicatedDocumentApplier,
                DefaultReplicatedDocumentApplier
            >();
            builder.ServiceCollection.TryAddSingleton<
                IControlPlaneReplica,
                DefaultControlPlaneReplica
            >();
            builder.ServiceCollection.TryAddSingleton<
                IDirectoryOutboxSignal,
                DefaultDirectoryOutboxSignal
            >();
            builder.ServiceCollection.TryAddSingleton<IDirectoryOutbox, DefaultDirectoryOutbox>();

            // The persistence slice registers a policy and a notifier that publish nothing. Both are replaced by the
            // one default instance, whichever registration runs first.
            builder.ServiceCollection.TryAddSingleton<DefaultDirectoryOutboxPolicy>();
            var policyDescriptor = ServiceDescriptor.Singleton<IDirectoryOutboxPolicy>(provider =>
                provider.GetRequiredService<DefaultDirectoryOutboxPolicy>()
            );
            builder.ServiceCollection.Replace(policyDescriptor);
            var notifierDescriptor = ServiceDescriptor.Singleton<IDirectoryOutboxNotifier>(
                provider => provider.GetRequiredService<DefaultDirectoryOutboxPolicy>()
            );
            builder.ServiceCollection.Replace(notifierDescriptor);
            builder.ServiceCollection.TryAddSingleton<
                IDirectoryFeedCheckpoint,
                DefaultDirectoryFeedCheckpoint
            >();

            return builder;
        }
    }
}
