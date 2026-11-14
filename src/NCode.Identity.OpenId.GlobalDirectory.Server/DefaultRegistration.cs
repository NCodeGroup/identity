// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides extension methods to register the global directory server.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to register the services in.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the directory server's store as the <see cref="ITenantDirectory"/>, <see cref="ITeamDirectory"/>,
        /// <see cref="ICellDirectory"/> and <see cref="IPlatformDirectory"/> that <see cref="DirectoryEndpoints"/>
        /// serves, and binds <see cref="DirectoryServerOptions"/> from the <c>DirectoryServer</c> section, which must
        /// name the issuer and is validated when the host starts. The host registers the
        /// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> of <see cref="DirectoryDbContext"/>
        /// and the <c>IIssuerAccessTokenValidator</c> of the OpenID client library.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> holding the <c>DirectoryServer</c> section.</param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddOpenIdDirectoryServer(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection(DirectoryServerOptions.SectionName);
            serviceCollection
                .AddOptions<DirectoryServerOptions>()
                .Configure(options => section.Bind(options))
                .Validate(
                    options => !string.IsNullOrEmpty(options.Issuer),
                    $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.Issuer)} is required."
                )
                .ValidateOnStart();

            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<DefaultDirectoryStore>();
            serviceCollection.TryAddSingleton<ITenantDirectory>(serviceProvider =>
                serviceProvider.GetRequiredService<DefaultDirectoryStore>()
            );
            serviceCollection.TryAddSingleton<ITeamDirectory>(serviceProvider =>
                serviceProvider.GetRequiredService<DefaultDirectoryStore>()
            );
            serviceCollection.TryAddSingleton<ICellDirectory>(serviceProvider =>
                serviceProvider.GetRequiredService<DefaultDirectoryStore>()
            );
            serviceCollection.TryAddSingleton<IPlatformDirectory>(serviceProvider =>
                serviceProvider.GetRequiredService<DefaultDirectoryStore>()
            );

            return serviceCollection;
        }
    }
}
