// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace NCode.Identity.AspNetCore;

/// <summary>
/// Answers a problem document that asks the caller to wait. The wait goes in the <c>Retry-After</c> header (RFC 9110
/// §10.2.3) and in the problem's <c>retryAfterSeconds</c> extension member (RFC 9457 §3.2), so a client whose fetch
/// layer drops headers still reads it.
/// </summary>
// See ADR-1483.
[PublicAPI]
public static class RetryAfterProblem
{
    /// <summary>
    /// The name of the problem extension member that holds the wait in whole seconds, the same value as the
    /// <c>Retry-After</c> header.
    /// </summary>
    public const string MemberName = "retryAfterSeconds";

    /// <summary>
    /// Gets the whole seconds of a wait that a problem asks for: rounded up, and at least one, so the caller is never
    /// told to retry at once.
    /// </summary>
    /// <param name="wait">The time the caller waits before it retries.</param>
    /// <returns>The whole seconds of the wait.</returns>
    public static long GetWaitSeconds(TimeSpan wait)
    {
        var ceiling = Math.Ceiling(wait.TotalSeconds);
        return Math.Max(1L, (long)ceiling);
    }

    /// <summary>
    /// Sets the <c>Retry-After</c> header and the problem's <see cref="MemberName"/> member to the whole seconds of the
    /// wait, as <see cref="GetWaitSeconds"/> counts them, and returns the problem.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> whose response receives the header.</param>
    /// <param name="wait">The time the caller waits before it retries.</param>
    /// <param name="problem">The problem to answer, which receives the member.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem.</returns>
    public static ProblemHttpResult Create(
        HttpContext httpContext,
        TimeSpan wait,
        ProblemDetails problem
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);

        var seconds = GetWaitSeconds(wait);

        httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        problem.Extensions[MemberName] = seconds;

        return TypedResults.Problem(problem);
    }
}
