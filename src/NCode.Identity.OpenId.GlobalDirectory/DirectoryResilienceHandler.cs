// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Polly.Timeout;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Bounds every request to the directory service with the resilience pipeline that
/// <see cref="DirectoryResiliencePipelines"/> selects for it, and turns the <see cref="TimeoutRejectedException"/> that
/// a resilience timeout throws into the <see cref="TaskCanceledException"/> with a <see cref="TimeoutException"/> inner
/// exception that <see cref="HttpClient.Timeout"/> produces, so a caller that catches
/// <see cref="OperationCanceledException"/> does not let a timeout escape.
/// </summary>
/// <param name="pipelines">The pipelines that bound the requests.</param>
// See ADR-1385 and ADR-1336.
internal sealed class DirectoryResilienceHandler(DirectoryResiliencePipelines pipelines)
    : DelegatingHandler
{
    private DirectoryResiliencePipelines Pipelines { get; } = pipelines;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var pipeline = Pipelines.Select(request);

        try
        {
            return await pipeline.ExecuteAsync(
                static (state, token) => state.Handler.SendAttemptAsync(state.Request, token),
                (Handler: this, Request: request),
                cancellationToken
            );
        }
        catch (TimeoutRejectedException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException(exception.Message, exception);
            throw new TaskCanceledException(
                "The request to the directory timed out.",
                timeoutException
            );
        }
    }

    /// <summary>
    /// Sends one attempt of the request through the inner handler. The resilience pipeline calls it once per attempt,
    /// so a retried request is sent again.
    /// </summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/> to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// response of the inner handler.</returns>
    private async ValueTask<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => await base.SendAsync(request, cancellationToken);
}
