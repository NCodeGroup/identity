// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Registration;

namespace NCode.Identity.Mail.Mime;

/// <summary>
/// Provides extension methods for the <see cref="MailLibrary"/> builder to register the MIME form of mail messages and
/// the transports that send it.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The mail-library builder returned by <c>AddMailServices</c>.</param>
    extension(IServiceBuilder<MailLibrary> builder)
    {
        /// <summary>
        /// Registers the <see cref="IMimeMessageFactory"/> that builds the MIME form of a mail message, and the system
        /// <see cref="TimeProvider"/> that dates its messages, each only when none is registered.
        /// </summary>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        [PublicAPI]
        public IServiceBuilder<MailLibrary> AddMimeMessageFactory()
        {
            ArgumentNullException.ThrowIfNull(builder);

            var serviceCollection = builder.ServiceCollection;
            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<IMimeMessageFactory, DefaultMimeMessageFactory>();

            return builder;
        }

        /// <summary>
        /// Registers a transport that sends MIME as the host's one mail transport: adds the
        /// <see cref="MailTransportRegistration"/> named <paramref name="name"/>, registers the
        /// <see cref="IMimeMessageFactory"/> as <c>AddMimeMessageFactory</c> does, and replaces the registered
        /// <see cref="IMailTransport"/> with <typeparamref name="TTransport"/>.
        /// </summary>
        /// <typeparam name="TTransport">The type of the transport that sends MIME.</typeparam>
        /// <param name="name">The name of the transport, such as the name of its configuration section.</param>
        /// <returns>The same <see cref="IServiceBuilder{T}"/> so that calls can be chained.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when another mail transport is registered.</exception>
        [PublicAPI]
        public IServiceBuilder<MailLibrary> AddMimeMailTransport<TTransport>(string name)
            where TTransport : class, IMailTransport
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(name);

            var serviceCollection = builder.ServiceCollection;

            var registered = serviceCollection.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(MailTransportRegistration)
            );
            if (registered is not null)
            {
                var registeredName =
                    (registered.ImplementationInstance as MailTransportRegistration)?.Name
                    ?? "(unknown)";
                throw new InvalidOperationException(
                    $"The mail transport '{registeredName}' is registered, so the mail transport '{name}' cannot be; a host configures one mail transport."
                );
            }

            var marker = new MailTransportRegistration(name);
            serviceCollection.AddSingleton(marker);

            builder.AddMimeMessageFactory();

            var transportDescriptor = ServiceDescriptor.Singleton<IMailTransport, TTransport>();
            serviceCollection.Replace(transportDescriptor);

            return builder;
        }
    }
}
