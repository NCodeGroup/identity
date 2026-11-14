// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using NCode.Buffers;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    string? Authorization,
    string? Body
);

internal sealed class StubHttpMessageHandler(
    Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond
) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public static StubHttpMessageHandler Respond(HttpStatusCode statusCode, string body = "") =>
        new((_, _) => Task.FromResult(CreateResponse(statusCode, body)));

    public static StubHttpMessageHandler Throw(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, SecureEncoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            body
        );
        Requests.Add(recorded);

        return await respond(recorded, cancellationToken);
    }
}
