// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one call that a cell makes to the directory service: the HTTP method, the absolute URI, and an optional
/// JSON body.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The absolute URI of the endpoint.</param>
internal sealed record CellApiRequest(HttpMethod Method, Uri Uri)
{
    /// <summary>
    /// Gets the record to send as the JSON body, or <c>null</c> for a request without a body.
    /// </summary>
    public object? Body { get; init; }
}
