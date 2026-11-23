// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace NCode.Identity.Mail;

/// <summary>
/// Provides the registered <see cref="IMailComposer"/> of each model type, so the sender refuses a model that no
/// composer handles before it queues it and the delivery job composes the message of the model it carries.
/// </summary>
/// <param name="composers">The registered composers, one per model type.</param>
// See ADR-1532.
internal sealed class MailComposerRegistry(IEnumerable<IMailComposer> composers)
{
    private FrozenDictionary<Type, IMailComposer> Composers { get; } = CreateComposers(composers);

    /// <summary>
    /// Gets the composer that composes models of type <paramref name="modelType"/>.
    /// </summary>
    /// <param name="modelType">The type of the model.</param>
    /// <returns>The registered <see cref="IMailComposer"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no composer is registered for
    /// <paramref name="modelType"/>.</exception>
    public IMailComposer Get(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (Composers.TryGetValue(modelType, out var composer))
            return composer;

        throw new InvalidOperationException(
            $"No mail composer is registered for the model type '{modelType}'."
        );
    }

    private static FrozenDictionary<Type, IMailComposer> CreateComposers(
        IEnumerable<IMailComposer> composers
    )
    {
        ArgumentNullException.ThrowIfNull(composers);

        var result = new Dictionary<Type, IMailComposer>();
        foreach (var composer in composers)
        {
            if (!result.TryAdd(composer.ModelType, composer))
            {
                throw new InvalidOperationException(
                    $"More than one mail composer is registered for the model type '{composer.ModelType}'."
                );
            }
        }

        return result.ToFrozenDictionary();
    }
}
