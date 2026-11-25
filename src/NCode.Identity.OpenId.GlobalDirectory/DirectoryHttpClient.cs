// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NCode.Buffers;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Sends the requests of the remote adapters to the directory service and to the control plane with a bearer token,
/// over the directory's own HTTP client, which trusts only <see cref="DirectoryClientOptions.CaCertificatesPath"/> when
/// it is set. Each call, its response body read included, is bounded by
/// <see cref="DirectoryClientOptions.RequestTimeout"/>. It fails closed: a transport failure, a timeout, a <c>5xx</c>
/// answer, an oversized body, a token that cannot be obtained or any status other than a success, <c>404</c> or
/// <c>409</c> is a <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1285 and ADR-1385 (the client's pipeline runs on Polly.Core alone).
internal sealed class DirectoryHttpClient(
    IHttpClientFactory httpClientFactory,
    ICellTokenSource tokenSource,
    IOptions<DirectoryClientOptions> options
)
{
    private const int ReadChunkBytes = 4096;

    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;

    private ICellTokenSource TokenSource { get; } = tokenSource;

    private Uri BaseUri { get; } = CreateBaseUri(options.Value.Url);

    private TimeSpan RequestTimeout { get; } = options.Value.RequestTimeout;

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
    /// <exception cref="DirectoryUnavailableException">
    /// The directory cannot be reached or answers with a failure.
    /// </exception>
    public ValueTask<DirectoryResponse> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken
    )
    {
        var uri = new Uri(BaseUri, DirectoryClientDefaults.ApiPrefix + path);
        var cellRequest = new CellApiRequest(method, uri) { Body = body };
        return SendAsync(cellRequest, cancellationToken);
    }

    /// <summary>
    /// Sends a request to the directory service or to the control plane.
    /// </summary>
    /// <param name="cellRequest">The call to make.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="DirectoryResponse"/> of a success, <c>404</c> or <c>409</c>.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The endpoint cannot be reached or answers with a failure.
    /// </exception>
    public async ValueTask<DirectoryResponse> SendAsync(
        CellApiRequest cellRequest,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(cellRequest);

        var method = cellRequest.Method;
        var uri = cellRequest.Uri;
        var accessToken = await TokenSource.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var acceptJson = new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json);
        request.Headers.Accept.Add(acceptJson);
        if (cellRequest.Body is { } body)
        {
            var bodyType = body.GetType();
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(
                body,
                bodyType,
                DirectoryJson.Options
            );
            var content = new ByteArrayContent(bodyBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json);
            request.Content = content;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutSource.CancelAfter(RequestTimeout);

        try
        {
            var client = HttpClientFactory.CreateClient(DirectoryClientDefaults.HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token
            );

            var status = response.StatusCode;
            if ((int)status >= (int)HttpStatusCode.InternalServerError)
                throw new DirectoryUnavailableException(
                    $"{uri} answered {(int)status} to {method}."
                );

            var bytes = await ReadBodyAsync(response.Content, uri, timeoutSource.Token);
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

    /// <summary>
    /// Creates the base URI of the directory service from its configured URL, with a trailing slash so that relative
    /// paths append to it.
    /// </summary>
    /// <param name="url">The configured URL of the directory service.</param>
    /// <returns>The absolute base <see cref="Uri"/>.</returns>
    /// <exception cref="InvalidOperationException">The URL is not set.</exception>
    private static Uri CreateBaseUri(string? url)
    {
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is not set."
            );

        var endsWithSlash = url.EndsWith('/');
        return new Uri(endsWithSlash ? url : url + '/', UriKind.Absolute);
    }

    /// <summary>
    /// Reads the body of a response, up to the largest body that the client accepts.
    /// </summary>
    /// <param name="content">The <see cref="HttpContent"/> of the response.</param>
    /// <param name="uri">The URI that answered, which names the endpoint in an error.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The bytes of the body.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The body is larger than the largest body that the client accepts.
    /// </exception>
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
        using var buffer = BufferFactory.CreatePooledStream(isSensitive: false);
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                return buffer.WrittenSpan.ToArray();

            if (buffer.Length + read > DirectoryClientDefaults.MaxResponseBytes)
                throw new DirectoryUnavailableException(
                    $"{uri} answered with a body larger than {DirectoryClientDefaults.MaxResponseBytes} bytes."
                );

            buffer.Write(chunk, 0, read);
        }
    }
}
