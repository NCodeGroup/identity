// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace NCode.Identity.AspNetCore;

// See ADR-1520.
/// <summary>
/// Decides, before an endpoint reads its request, whether the endpoint runs. A gate runs before minimal APIs bind the
/// endpoint's parameters, its body included, and before its endpoint filters, so a request it refuses costs no more
/// than its headers. <c>AddEndpointGate&lt;TGate&gt;()</c> adds one, and one instance is created for each request from
/// the request's services.
/// </summary>
[PublicAPI]
public interface IEndpointGate
{
    /// <summary>
    /// Asynchronously decides whether the endpoint runs.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> associated with the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The <see cref="IResult"/> that answers the request instead of the endpoint, or <see langword="null"/>
    /// to let the endpoint run.</returns>
    ValueTask<IResult?> InvokeAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
