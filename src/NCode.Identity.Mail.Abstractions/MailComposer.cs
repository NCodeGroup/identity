// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Provides the base of a composer that composes the mail message for a model of type <typeparamref name="TModel"/>.
/// </summary>
/// <typeparam name="TModel">The type of the model, which is serializable data.</typeparam>
// See ADR-1532.
[PublicAPI]
public abstract class MailComposer<TModel> : IMailComposer
    where TModel : class
{
    /// <inheritdoc />
    public Type ModelType => typeof(TModel);

    /// <summary>
    /// Composes the mail message for <paramref name="model"/>.
    /// </summary>
    /// <param name="model">The model to compose the message for.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the message, which the caller owns and disposes.
    /// </returns>
    public abstract ValueTask<MailMessage> ComposeAsync(
        TModel model,
        CancellationToken cancellationToken
    );

    /// <inheritdoc />
    ValueTask<MailMessage> IMailComposer.ComposeAsync(
        object model,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not TModel typedModel)
        {
            var modelType = model.GetType();
            throw new ArgumentException(
                $"The composer composes models of type '{typeof(TModel)}', not '{modelType}'.",
                nameof(model)
            );
        }

        return ComposeAsync(typedModel, cancellationToken);
    }
}
