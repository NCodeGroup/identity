// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;
using NCode.Mediator;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Sends the command that follows a committed directory write, in a service scope of its own and on a token bounded by
/// <see cref="PostCommitWork.Timeout"/>, so a command that fails never fails the committed write.
/// </summary>
// See ADR-1351.
internal interface IPostCommitCommandSender
{
    /// <summary>
    /// Sends <paramref name="command"/> in a service scope of its own, on a token bounded by
    /// <see cref="PostCommitWork.Timeout"/>. A failure, the bound's cancellation included, is passed to
    /// <paramref name="onFailure"/> and never fails the committed write. The caller's token never reaches the command;
    /// it only decides whether a cancellation is the caller's own, which propagates.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="onFailure">The action that receives the exception of a command that failed.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> of the caller, whose cancellation propagates.
    /// </param>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask SendAsync<TCommand>(
        TCommand command,
        Action<Exception> onFailure,
        CancellationToken cancellationToken
    )
        where TCommand : struct, ICommand;
}
