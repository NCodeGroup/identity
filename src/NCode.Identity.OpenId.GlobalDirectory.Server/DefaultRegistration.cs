// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.Server.Options;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides extension methods to register the global directory server.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder{T}"/> returned by <c>AddOpenIdClientLibrary</c>.</param>
    extension(IServiceBuilder<OpenIdClientLibrary> builder)
    {
        /// <summary>
        /// Registers the directory server's store as the <see cref="ITenantDirectory"/>, <see cref="ITeamDirectory"/>,
        /// <see cref="ICellDirectory"/> and <see cref="IPlatformDirectory"/> that <see cref="DirectoryEndpoints"/>
        /// serves, and binds <see cref="DirectoryServerOptions"/> strictly from the <c>DirectoryServer</c> section,
        /// which must name the issuer and is validated when the host starts. The host registers the
        /// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> of <see cref="DirectoryDbContext"/>
        /// and the OpenID client library, whose <c>IIssuerAccessTokenValidator</c> validates the bearer tokens. The
        /// host's <c>AllowedHosts</c> is validated when the host starts: it must list the deployment's host names and
        /// not be empty or contain <c>*</c>, unless <c>LocalDevelopment:Enabled</c> is true.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> holding the <c>DirectoryServer</c> section.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        public IServiceBuilder<OpenIdClientLibrary> AddOpenIdDirectoryServer(
            IConfiguration configuration
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);

            var serviceCollection = builder.ServiceCollection;

            serviceCollection.AddHostFilteringValidation(configuration);

            var section = configuration.GetSection(DirectoryServerOptions.SectionName);
            serviceCollection
                .AddOptions<DirectoryServerOptions>()
                .Configure(options => section.BindStrictly(options))
                .ValidateOnStart();
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IValidateOptions<DirectoryServerOptions>,
                    DirectoryServerOptionsValidator
                >()
            );

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

            return builder;
        }
    }
}
