// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.Mail.Mime;
using NCode.Identity.Mail.Smtp.Facades;
using NCode.Identity.Mail.Smtp.Options;
using NCode.Identity.Registration;

namespace NCode.Identity.Mail.Smtp;

/// <summary>
/// Provides opt-in extension methods for the <see cref="MailLibrary"/> builder to register the SMTP transport.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The mail-library builder returned by <c>AddMailServices</c>.</param>
    extension(IServiceBuilder<MailLibrary> builder)
    {
        /// <summary>
        /// Registers the SMTP transport in place of the registered one when the <c>Mail:Smtp</c> section exists, bound
        /// strictly and validated as the host starts; without the section it registers nothing.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> holding the <c>Mail:Smtp</c> section.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the section exists and another mail transport is already registered.
        /// </exception>
        [PublicAPI]
        public IServiceBuilder<MailLibrary> AddSmtpMailTransport(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection(SmtpMailTransportOptions.SectionName);
            if (!section.Exists())
                return builder;

            var serviceCollection = builder.ServiceCollection;

            builder.AddMimeMailTransport<SmtpMailTransport>(SmtpMailTransportOptions.SectionName);

            // The validator refuses clear-text submission unless the host is configured for local development
            // (ADR-1532, ADR-1370).
            serviceCollection.AddLocalDevelopmentOptions(configuration);

            serviceCollection
                .AddOptions<SmtpMailTransportOptions>()
                .Configure(options => section.BindStrictly(options))
                .ValidateOnStart();

            var validatorDescriptor = ServiceDescriptor.Singleton<
                IValidateOptions<SmtpMailTransportOptions>,
                SmtpMailTransportOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(validatorDescriptor);

            serviceCollection.TryAddSingleton<ISmtpClientFactory, DefaultSmtpClientFactory>();

            return builder;
        }
    }
}
