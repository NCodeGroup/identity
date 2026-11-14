// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Client;

/// <summary>
/// Represents the outcome of validating an access token that another issuer issued: either its verified payload or the
/// reason it is invalid.
/// </summary>
[PublicAPI]
public sealed class IssuerAccessTokenResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IssuerAccessTokenResult"/> class for a valid access token.
    /// </summary>
    /// <param name="payload">The verified payload of the access token.</param>
    /// <param name="issuer">The verified <c>iss</c> claim of the access token.</param>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> is null or empty.</exception>
    public IssuerAccessTokenResult(JsonElement payload, string issuer)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);

        Payload = payload;
        Issuer = issuer;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IssuerAccessTokenResult"/> class for an invalid access token.
    /// </summary>
    /// <param name="failureCode">The machine-readable code of the reason the access token is invalid.</param>
    /// <param name="failureReason">The reason the access token is invalid.</param>
    /// <exception cref="ArgumentException"><paramref name="failureCode"/> or <paramref name="failureReason"/> is null or empty.</exception>
    public IssuerAccessTokenResult(string failureCode, string failureReason)
    {
        ArgumentException.ThrowIfNullOrEmpty(failureCode);
        ArgumentException.ThrowIfNullOrEmpty(failureReason);

        FailureCode = failureCode;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Gets the verified payload when the access token is valid; otherwise, <see langword="default"/>.
    /// </summary>
    public JsonElement Payload { get; }

    /// <summary>
    /// Gets the verified <c>iss</c> claim when the access token is valid; otherwise, <see langword="null"/>.
    /// </summary>
    public string? Issuer { get; }

    /// <summary>
    /// Gets the machine-readable code of the reason the access token is invalid, a
    /// <see cref="TokenValidationFailureCodes"/> value or another code a validator reports; otherwise,
    /// <see langword="null"/>.
    /// </summary>
    public string? FailureCode { get; }

    /// <summary>
    /// Gets the reason the access token is invalid; otherwise, <see langword="null"/>.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// Gets a value indicating whether the access token is valid.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureCode), nameof(FailureReason))]
    [MemberNotNullWhen(true, nameof(Issuer))]
    public bool IsValid => FailureCode is null;

    /// <summary>
    /// Gets a value indicating whether the access token could not be judged because the issuer's discovery document or
    /// signing keys could not be retrieved, which is neither valid nor proven invalid.
    /// </summary>
    public bool IsUnavailable =>
        string.Equals(
            FailureCode,
            TokenValidationFailureCodes.IssuerUnavailable,
            StringComparison.Ordinal
        );
}
