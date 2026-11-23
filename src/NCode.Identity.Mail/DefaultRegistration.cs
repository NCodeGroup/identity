// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Dispatch;
using NCode.Identity.Registration;

namespace NCode.Identity.Mail;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the mail library.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add services to.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the mail library: the sender that queues each model for delivery through the job dispatcher, the
        /// registry of the composers that build each queued model's message at every delivery attempt, the job that
        /// delivers it, and a transport that refuses every message, which a registered transport such as
        /// <c>AddSmtpMailTransport</c> replaces.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> for the mail library so that calls can be chained.</returns>
        [PublicAPI]
        public IServiceBuilder<MailLibrary> AddMailServices()
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.AddMetrics();
            serviceCollection.TryAddSingleton<MailDeliveryMetrics>();
            serviceCollection.TryAddSingleton<MailComposerRegistry>();
            serviceCollection.TryAddSingleton<IMailSender, DefaultMailSender>();
            serviceCollection.TryAddSingleton<IMailTransport, UnconfiguredMailTransport>();

            // Mail delivery runs as a retryable dispatch job; the handler hands the message to the transport.
            var deliveryJobHandler = ServiceDescriptor.Scoped<
                IDispatchJobHandler<MailDeliveryJob>,
                MailDeliveryJobHandler
            >();
            serviceCollection.TryAddEnumerable(deliveryJobHandler);

            return serviceCollection.NewBuilder<MailLibrary>();
        }
    }
}
