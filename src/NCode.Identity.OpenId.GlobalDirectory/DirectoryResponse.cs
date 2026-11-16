// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Holds the status and the body of an answer of the directory service that is not a failure of the service. A
/// failure of the service never becomes a <see cref="DirectoryResponse"/>: it is a
/// <see cref="DirectoryUnavailableException"/>.
/// </summary>
internal sealed class DirectoryResponse(HttpStatusCode statusCode, byte[] body)
{
    /// <summary>
    /// Gets the HTTP status of the answer: a success, <c>404</c> or <c>409</c>.
    /// </summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    private byte[] Body { get; } = body;

    /// <summary>
    /// Gets a value indicating whether the status is a <c>2xx</c> success.
    /// </summary>
    public bool IsSuccess =>
        StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;

    /// <summary>
    /// Gets a value indicating whether the directory did not find what was asked for.
    /// </summary>
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>
    /// Gets a value indicating whether the directory refused the request because of a conflict.
    /// </summary>
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    /// <summary>
    /// Requires a success: any other answer to a call that has no meaning for it is a failure of the directory.
    /// </summary>
    /// <param name="operation">The description of the call, for the message.</param>
    /// <exception cref="DirectoryUnavailableException">The status is not a success.</exception>
    public void EnsureSuccess(string operation)
    {
        if (!IsSuccess)
            throw new DirectoryUnavailableException(
                $"The directory answered {(int)StatusCode} to {operation}."
            );
    }

    /// <summary>
    /// Reads the body as a record of the directory API.
    /// </summary>
    /// <typeparam name="T">The type of the record.</typeparam>
    /// <returns>The record.</returns>
    /// <exception cref="DirectoryUnavailableException">The body is not the record.</exception>
    public T Read<T>()
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(Body, DirectoryJson.Options)
                ?? throw new DirectoryUnavailableException(
                    "The directory answered with an empty body."
                );
        }
        catch (JsonException exception)
        {
            throw new DirectoryUnavailableException(
                "The directory answered with a body that is not the record that the call reads.",
                exception
            );
        }
    }

    /// <summary>
    /// Reads the <c>detail</c> of an RFC 9457 problem document.
    /// </summary>
    /// <param name="fallback">The text to return when the body carries no <c>detail</c>.</param>
    /// <returns>The detail of the problem, or <paramref name="fallback"/>.</returns>
    public string ReadProblemDetail(string fallback)
    {
        try
        {
            var problem = JsonSerializer.Deserialize<JsonElement>(Body, DirectoryJson.Options);
            if (
                problem.ValueKind == JsonValueKind.Object
                && problem.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && detail.GetString() is { Length: > 0 } text
            )
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // a body that is not a problem document carries no detail
        }

        return fallback;
    }
}
