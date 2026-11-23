// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.Mail.AmazonSes.Facades;
using NCode.Identity.Mail.AmazonSes.Options;
using NCode.Identity.Mail.Mime;
using NCode.Identity.Registration;

namespace NCode.Identity.Mail.AmazonSes;

/// <summary>
/// Provides opt-in extension methods for the <see cref="MailLibrary"/> builder to register the Amazon SES transport.
/// </summary>
// See ADR-1528.
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The mail-library builder returned by <c>AddMailServices</c>.</param>
    extension(IServiceBuilder<MailLibrary> builder)
    {
        /// <summary>
        /// Registers the Amazon SES transport in place of the registered one when the <c>Mail:AmazonSes</c> section
        /// exists, bound strictly and validated as the host starts; without the section it registers nothing.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> holding the <c>Mail:AmazonSes</c>
        /// section.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the section exists and another mail transport is already registered.
        /// </exception>
        [PublicAPI]
        public IServiceBuilder<MailLibrary> AddAmazonSesMailTransport(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection(AmazonSesMailTransportOptions.SectionName);
            if (!section.Exists())
                return builder;

            var serviceCollection = builder.ServiceCollection;

            builder.AddMimeMailTransport<AmazonSesMailTransport>(
                AmazonSesMailTransportOptions.SectionName
            );

            serviceCollection
                .AddOptions<AmazonSesMailTransportOptions>()
                .Configure(options => section.BindStrictly(options))
                .ValidateOnStart();

            var validatorDescriptor = ServiceDescriptor.Singleton<
                IValidateOptions<AmazonSesMailTransportOptions>,
                AmazonSesMailTransportOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(validatorDescriptor);

            serviceCollection.TryAddSingleton<
                IAmazonSesClientFactory,
                DefaultAmazonSesClientFactory
            >();

            return builder;
        }
    }
}
