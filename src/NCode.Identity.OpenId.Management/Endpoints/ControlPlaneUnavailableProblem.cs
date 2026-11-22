// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// The answer of the management <c>/api</c> surface to a request whose route cannot reach the control plane that holds
/// the platform and team roles: <c>503 Service Unavailable</c> with a <c>Retry-After</c>.
/// </summary>
internal static class ControlPlaneUnavailableProblem
{
    /// <summary>
    /// Creates the <c>503 Service Unavailable</c> answer for a request whose route cannot reach the control plane that
    /// holds the platform and team roles, and sets its <c>Retry-After</c> header on the response.
    /// </summary>
    /// <param name="httpContext">The request that the answer is for.</param>
    /// <returns>The <see cref="IResult"/> that answers the request.</returns>
    public static IResult Create(HttpContext httpContext)
    {
        var retryAfter = TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds);
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Detail = "The control plane that holds the platform and team roles is unavailable.",
        };

        return RetryAfterProblem.Create(httpContext, retryAfter, problem);
    }
}
