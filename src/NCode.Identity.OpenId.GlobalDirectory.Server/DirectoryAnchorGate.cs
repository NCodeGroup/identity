// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NCode.Identity.AspNetCore;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// The gate that holds every route of the directory API until <see cref="DirectoryAnchorInitializer"/> has committed
/// the platform's trust anchor. A request that arrives earlier is answered <c>503 Service Unavailable</c> through
/// <see cref="RetryAfterProblem"/>, with the wait of
/// <see cref="GlobalDirectoryProtocol.UnavailableRetryAfterSeconds"/>.
/// </summary>
// See ADR-1536.
internal sealed class DirectoryAnchorGate(DirectoryAnchorState state) : IEndpointGate
{
    private DirectoryAnchorState State { get; } = state;

    /// <inheritdoc />
    public ValueTask<IResult?> InvokeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (State.IsInitialized)
            return ValueTask.FromResult<IResult?>(null);

        var retryAfter = TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds);
        var problem = new ProblemDetails
        {
            Title = "Service Unavailable",
            Detail = "The directory is starting. Retry the request.",
            Status = StatusCodes.Status503ServiceUnavailable,
        };
        IResult result = RetryAfterProblem.Create(httpContext, retryAfter, problem);
        return ValueTask.FromResult<IResult?>(result);
    }
}
