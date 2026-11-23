// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Builds the error a request shed by the admission gate is answered with: <c>503 Service Unavailable</c> (RFC 9110
/// §15.6.4) with the <c>temporarily_unavailable</c> error code, a <c>Retry-After</c> in whole seconds (RFC 9110
/// §10.2.3), and <c>Cache-Control: no-store</c>.
/// </summary>
// See ADR-1523.
internal static class AdmissionErrors
{
    /// <summary>
    /// Creates the error for a shed request.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the request.</param>
    /// <param name="retryAfter">How long the caller waits before retrying.</param>
    /// <returns>The <see cref="IOpenIdError"/> to answer the request with.</returns>
    public static IOpenIdError Shed(OpenIdContext openIdContext, TimeSpan retryAfter)
    {
        // Retry-After counts whole seconds, so round up and never tell the caller to retry at once.
        var ceiling = Math.Ceiling(retryAfter.TotalSeconds);
        var seconds = Math.Max(1L, (long)ceiling);
        var retryAfterSeconds = seconds.ToString(CultureInfo.InvariantCulture);

        return openIdContext
            .ErrorFactory.Create(OpenIdProtocol.ErrorCodes.TemporarilyUnavailable)
            .WithDescription("The server is temporarily overloaded; retry later.")
            .WithStatusCode(StatusCodes.Status503ServiceUnavailable)
            .WithResponseHeader(HeaderNames.RetryAfter, retryAfterSeconds)
            .WithResponseHeader(HeaderNames.CacheControl, "no-store");
    }
}
