// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence;
using NCode.Mediator;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IPostCommitCommandSender"/>. The mediator is scoped (its handlers'
/// unit of work is) and the feed runs outside any request, so each send gets a service scope of its own.
/// </summary>
/// <param name="serviceScopeFactory">
/// The <see cref="IServiceScopeFactory"/> that creates the scope of each send.
/// </param>
/// <param name="timeProvider">The <see cref="TimeProvider"/> that measures the post-commit bound.</param>
// See ADR-1351.
internal sealed class DefaultPostCommitCommandSender(
    IServiceScopeFactory serviceScopeFactory,
    TimeProvider timeProvider
) : IPostCommitCommandSender
{
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public async ValueTask SendAsync<TCommand>(
        TCommand command,
        Action<Exception> onFailure,
        CancellationToken cancellationToken
    )
        where TCommand : struct, ICommand
    {
        await IsolatedStep.RunAsync(
            () =>
                PostCommitWork.RunAsync(
                    TimeProvider,
                    static async (state, token) =>
                    {
                        await using var scope = state.ServiceScopeFactory.CreateAsyncScope();
                        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                        await mediator.SendAsync(state.Command, token);
                    },
                    (ServiceScopeFactory: ServiceScopeFactory, Command: command)
                ),
            onFailure,
            cancellationToken
        );
    }
}
