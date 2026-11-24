// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection.Extensions;
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
                IDirectoryFeedCheckpoint,
                DefaultDirectoryFeedCheckpoint
            >();

            return builder;
        }
    }
}
