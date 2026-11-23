// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Sends mail on behalf of the server's features: it accepts a serializable model for delivery out of band, retrying a
/// delivery that fails, and returns once the model is queued, so the caller never waits on the mail provider. A
/// registered <see cref="IMailComposer"/> of the model's type composes the message at each delivery attempt.
/// </summary>
// See ADR-1532.
[PublicAPI]
public interface IMailSender
{
    /// <summary>
    /// Queues <paramref name="model"/> for delivery. The model is serializable data, and a registered
    /// <see cref="IMailComposer"/> of its type composes the message from it at each delivery attempt.
    /// </summary>
    /// <typeparam name="TModel">The type of the model.</typeparam>
    /// <param name="model">The model to compose the message from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once the model is queued.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no composer is registered for the type of the
    /// model.</exception>
    ValueTask SendAsync<TModel>(TModel model, CancellationToken cancellationToken)
        where TModel : class;
}
