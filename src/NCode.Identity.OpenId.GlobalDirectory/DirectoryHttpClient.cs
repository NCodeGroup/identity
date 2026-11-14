// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Sends the requests of the remote adapters to the directory service and to the control plane with a bearer token,
/// over the protocol client's HTTP client, which bounds every call with a total and a per-attempt timeout and retries
/// only safe requests. It fails closed: a transport failure, a timeout, a <c>5xx</c> answer, an oversized body, a
/// token that cannot be obtained or any status other than a success, <c>404</c> or <c>409</c> is a
/// <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1285 and ADR-1385 (the client's pipeline runs on Polly.Core alone).
internal sealed class DirectoryHttpClient(
    IHttpClientFactory httpClientFactory,
    ICellTokenSource tokenSource,
    IOptions<DirectoryClientOptions> options
)
{
    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;

    private ICellTokenSource TokenSource { get; } = tokenSource;

    private Uri BaseUri { get; } = CreateBaseUri(options.Value.Url);

    /// <summary>
    /// Escapes a value for use as one segment of a directory path.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped segment.</returns>
    public static string Segment(string value) => Uri.EscapeDataString(value);

    /// <summary>
    /// Sends a request to the directory API.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path under <c>directory/v1/</c>, with its query.</param>
    /// <param name="body">The record to send as the JSON body, or <c>null</c> for a request without a body.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="DirectoryResponse"/> of a success, <c>404</c> or <c>409</c>.</returns>
    /// <exception cref="DirectoryUnavailableException">The directory cannot be reached or answers with a failure.</exception>
    public ValueTask<DirectoryResponse> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken
    ) =>
        SendAsync(
            new CellApiRequest(
                method,
                new Uri(BaseUri, DirectoryClientDefaults.ApiPrefix + path),
                DirectoryClientDefaults.Audience,
                DirectoryClientDefaults.Scope
            )
            {
                Body = body,
            },
            cancellationToken
        );

    /// <summary>
    /// Sends a request to the directory service or to the control plane.
    /// </summary>
    /// <param name="cellRequest">The call to make.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="DirectoryResponse"/> of a success, <c>404</c> or <c>409</c>.</returns>
    /// <exception cref="DirectoryUnavailableException">The endpoint cannot be reached or answers with a failure.</exception>
    public async ValueTask<DirectoryResponse> SendAsync(
        CellApiRequest cellRequest,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(cellRequest);

        var method = cellRequest.Method;
        var uri = cellRequest.Uri;
        var accessToken = await TokenSource.GetAccessTokenAsync(
            cellRequest.Audience,
            cellRequest.Scope,
            cancellationToken
        );

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (cellRequest.Body is { } body)
        {
            var content = new ByteArrayContent(
                JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), DirectoryJson.Options)
            );
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
        }

        try
        {
            var client = HttpClientFactory.CreateClient(OpenIdClientDefaults.HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            var status = response.StatusCode;
            if ((int)status >= (int)HttpStatusCode.InternalServerError)
                throw new DirectoryUnavailableException(
                    $"{uri} answered {(int)status} to {method}."
                );

            var bytes = await ReadBodyAsync(response.Content, uri, cancellationToken);
            var result = new DirectoryResponse(status, bytes);
            if (!result.IsSuccess && !result.IsNotFound && !result.IsConflict)
                throw new DirectoryUnavailableException(
                    $"{uri} answered {(int)status} to {method}."
                );

            return result;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new DirectoryUnavailableException(
                $"The request timed out: {method} {uri}.",
                exception
            );
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new DirectoryUnavailableException(
                $"The endpoint could not be reached: {method} {uri}.",
                exception
            );
        }
    }

    private static Uri CreateBaseUri(string? url)
    {
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is not set."
            );

        return new Uri(url.EndsWith('/') ? url : url + '/', UriKind.Absolute);
    }

    private static async ValueTask<byte[]> ReadBodyAsync(
        HttpContent content,
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > DirectoryClientDefaults.MaxResponseBytes)
            throw new DirectoryUnavailableException(
                $"{uri} answered with a body larger than {DirectoryClientDefaults.MaxResponseBytes} bytes."
            );

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                return buffer.ToArray();

            if (buffer.Length + read > DirectoryClientDefaults.MaxResponseBytes)
                throw new DirectoryUnavailableException(
                    $"{uri} answered with a body larger than {DirectoryClientDefaults.MaxResponseBytes} bytes."
                );

            buffer.Write(chunk, 0, read);
        }
    }
}
