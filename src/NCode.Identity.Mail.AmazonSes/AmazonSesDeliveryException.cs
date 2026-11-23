// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Mail.AmazonSes;

/// <summary>
/// The exception that is thrown when Amazon SES refuses a message. It carries the error code and the HTTP status of the
/// refusal and never the service's message text, which can echo the recipient's address.
/// </summary>
/// <param name="errorCode">The error code that Amazon SES replied with, or <see langword="null"/> for none.</param>
/// <param name="statusCode">The HTTP status that Amazon SES replied with.</param>
// See ADR-1528.
internal sealed class AmazonSesDeliveryException(string? errorCode, int statusCode)
    : Exception(
        $"Amazon SES refused the message with error code '{errorCode}' and status {statusCode}."
    )
{
    /// <summary>
    /// Gets the error code that Amazon SES replied with, or <see langword="null"/> for none.
    /// </summary>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>
    /// Gets the HTTP status that Amazon SES replied with.
    /// </summary>
    public int StatusCode { get; } = statusCode;
}
