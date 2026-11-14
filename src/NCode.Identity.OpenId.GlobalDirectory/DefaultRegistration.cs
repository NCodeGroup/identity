// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;
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
        /// Registers the remote <see cref="ITenantDirectory"/>, <see cref="ITeamDirectory"/> and
        /// <see cref="ICellDirectory"/> when the <c>Directory:Url</c> setting is set, and registers nothing otherwise,
        /// so a cell that runs on its own keeps the local adapters. The <c>Directory</c> section binds
        /// <see cref="DirectoryClientOptions"/> strictly and is validated when the host starts. Each adapter calls the
        /// <c>directory/v1</c> API with a client-credentials token for the audience <c>urn:ncode:directory</c>, which
        /// the OpenID client library requests as the cell's machine client from the issuer named by
        /// <c>Cell:ControlPlane:Issuer</c>, over the client library's bounded HTTP client. A call that cannot reach the
        /// directory throws <see cref="DirectoryUnavailableException"/>.
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
            serviceCollection.TryAddSingleton<IDirectoryTokenSource, DefaultDirectoryTokenSource>();
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
                ServiceDescriptor.Singleton<ICellDirectory, RemoteCellDirectory>()
            );

            return builder;
        }
    }
}
