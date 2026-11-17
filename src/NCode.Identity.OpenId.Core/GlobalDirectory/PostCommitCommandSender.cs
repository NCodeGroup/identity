// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence;
using NCode.Mediator;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Sends the command that follows a committed directory write. The mediator is scoped (its handlers' unit of work is)
/// and the feed runs outside any request, so each send gets a service scope of its own, on the post-commit bound
/// <see cref="PostCommitWork.Timeout"/>. The class is static, like <see cref="PostCommitWork"/>, because it holds no
/// state and its callers pass the scope factory and clock they already hold.
/// </summary>
// See ADR-1351.
internal static class PostCommitCommandSender
{
    /// <summary>
    /// Sends <paramref name="command"/> in a service scope of its own, on a token bounded by
    /// <see cref="PostCommitWork.Timeout"/>. A failure, the bound's cancellation included, is passed to
    /// <paramref name="onFailure"/> and never fails the committed write. The caller's token never reaches the command;
    /// it only decides whether a cancellation is the caller's own, which propagates.
    /// </summary>
    /// <param name="serviceScopeFactory">
    /// The <see cref="IServiceScopeFactory"/> that creates the scope of the send.
    /// </param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> that measures the post-commit bound.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="onFailure">The action that receives the exception of a command that failed.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> of the caller, whose cancellation propagates.
    /// </param>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public static async ValueTask SendAsync<TCommand>(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider,
        TCommand command,
        Action<Exception> onFailure,
        CancellationToken cancellationToken
    )
        where TCommand : struct, ICommand
    {
        await IsolatedStep.RunAsync(
            () =>
                PostCommitWork.RunAsync(
                    timeProvider,
                    static async (state, token) =>
                    {
                        await using var scope = state.ServiceScopeFactory.CreateAsyncScope();
                        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                        await mediator.SendAsync(state.Command, token);
                    },
                    (ServiceScopeFactory: serviceScopeFactory, Command: command)
                ),
            onFailure,
            cancellationToken
        );
    }
}
