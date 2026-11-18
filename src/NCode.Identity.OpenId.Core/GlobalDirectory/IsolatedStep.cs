// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Runs a step that follows a committed directory write and must never fail it. A failure of the step is handed to a
/// handler instead of propagating, and a cancellation propagates only when the caller's own token is cancelled, because
/// the step runs on a bounded token of its own whose timeout also surfaces as an
/// <see cref="OperationCanceledException"/>. The class is static, like <see cref="PostCommitWork"/>, because it holds
/// no state and its callers pass the step and the handler they already hold.
/// </summary>
// See ADR-1351.
internal static class IsolatedStep
{
    /// <summary>
    /// Runs <paramref name="step"/> and hands any exception it throws to <paramref name="onFailure"/>, so the failure
    /// never reaches the caller. An <see cref="OperationCanceledException"/> that <paramref name="step"/> throws while
    /// <paramref name="cancellationToken"/> is cancelled is the caller's own cancellation and propagates; one thrown
    /// while it is not, such as the timeout of the step's own bound, is handed to <paramref name="onFailure"/> like any
    /// other failure.
    /// </summary>
    /// <param name="step">The step to run.</param>
    /// <param name="onFailure">The action that receives the exception of a step that failed.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> of the caller, whose cancellation propagates.
    /// </param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public static async ValueTask RunAsync(
        Func<ValueTask> step,
        Action<Exception> onFailure,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await step();
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            onFailure(exception);
        }
    }
}
