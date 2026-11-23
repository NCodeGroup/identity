// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Composes the mail message for a model of one type each time a delivery is attempted.
/// </summary>
// See ADR-1532.
[PublicAPI]
public interface IMailComposer
{
    /// <summary>
    /// Gets the type of the model the composer composes a message for.
    /// </summary>
    Type ModelType { get; }

    /// <summary>
    /// Composes the message for <paramref name="model"/>.
    /// </summary>
    /// <param name="model">The model, which is an instance of <see cref="ModelType"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the message, which the caller owns and disposes.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is not an instance of
    /// <see cref="ModelType"/>.</exception>
    ValueTask<MailMessage> ComposeAsync(object model, CancellationToken cancellationToken);
}
