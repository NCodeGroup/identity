// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NCode.Buffers;
using NCode.Disposables;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;

namespace NCode.Identity.OpenId.Authentication.Clients.Handlers;

/// <summary>
/// Provides an implementation of <see cref="IClientAuthenticationHandler"/> that uses HTTP Basic Authentication.
/// </summary>
internal class ClientSecretBasicClientAuthenticationHandler(
    IOpenIdClientResolver clientResolver,
    IOpenIdClientFactory clientFactory,
    IAuditEventRecorder auditEventRecorder,
    IClientAuthenticationAttemptLimiter attemptLimiter,
    ILogger<ClientSecretBasicClientAuthenticationHandler> logger
)
    : CommonClientAuthenticationHandler(
        clientResolver,
        clientFactory,
        auditEventRecorder,
        attemptLimiter,
        logger
    ),
        IClientAuthenticationHandler
{
    private static string UriDecode(Span<char> source)
    {
        source.Replace('+', ' ');
        return Uri.UnescapeDataString(source);
    }

    private static IDisposable UriDecodeSensitive(
        ReadOnlyMemory<char> encoded,
        out ReadOnlyMemory<char> decoded
    )
    {
        var source = encoded.Span;
        if (!source.ContainsAny('%', '+'))
        {
            decoded = encoded;
            return Disposable.Empty;
        }

        var lease = BufferFactory.Rent<char>(
            encoded.Length,
            isSensitive: true,
            out Memory<char> buffer
        );
        // BufferFactory.Rent slices the buffer to exactly the encoded length, so the replace and the unescape run over
        // the encoded secret alone (RFC 6749 section 2.3.1).
        try
        {
            var destination = buffer.Span;
            source.Replace(destination, '+', ' ');

            var decodeResult = Uri.TryUnescapeDataString(
                destination,
                destination,
                out var unescapedLength
            );
            Debug.Assert(decodeResult);

            decoded = buffer[..unescapedLength];
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override string AuthenticationMethod =>
        OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic;

    /// <inheritdoc />
    protected override ClientAuthenticationResult CreateInvalidClientResult(
        OpenIdContext openIdContext
    )
    {
        // The client authenticated via the HTTP Authorization header, so RFC 6749 §5.2 requires a 401 response
        // with a WWW-Authenticate challenge matching the scheme the client used.
        var invalidClient = openIdContext
            .ErrorFactory.InvalidClient()
            .WithStatusCode(StatusCodes.Status401Unauthorized);
        var challenge = WwwAuthenticateChallenge.Basic(openIdContext.Tenant.Issuer);
        var error = invalidClient.WithResponseHeader(HeaderNames.WWWAuthenticate, challenge);
        return new(error);
    }

    /// <inheritdoc />
    public override async ValueTask<ClientAuthenticationResult> AuthenticateClientAsync(
        OpenIdContext openIdContext,
        CancellationToken cancellationToken
    )
    {
        var httpContext = openIdContext.Http;

        var authorizationHeader = httpContext.Request.Headers.Authorization;
        if (authorizationHeader.Count != 1)
            // not basic auth, let another handler try
            return ClientAuthenticationResult.Undefined;

        var authorizationValue = authorizationHeader[0].AsMemory();
        if (authorizationValue.IsEmpty)
            // not basic auth, let another handler try
            return ClientAuthenticationResult.Undefined;

        const string prefix = "Basic ";
        if (!authorizationValue.Span.StartsWith(prefix, StringComparison.Ordinal))
            // not basic auth, let another handler try
            return ClientAuthenticationResult.Undefined;

        var encodedCredentials = authorizationValue[prefix.Length..].Trim();
        if (encodedCredentials.IsEmpty)
            // The client attempted Basic authentication but supplied no credentials; this is a failed
            // authentication (invalid_client), which RFC 6749 §5.2 requires be answered with a 401 challenge. A header
            // that carries no credential verifies nothing, so it is answered without a count against the limit of
            // failed client authentications, as the two malformed shapes below are. See ADR-1399.
            return CreateInvalidClientResult(openIdContext);

        return await AuthenticateCredentialsAsync(
            openIdContext,
            encodedCredentials,
            cancellationToken
        );
    }

    private async ValueTask<ClientAuthenticationResult> AuthenticateCredentialsAsync(
        OpenIdContext openIdContext,
        ReadOnlyMemory<char> encodedCredentials,
        CancellationToken cancellationToken
    )
    {
        // RFC 7617 encodes the user-id/password with standard Base64 (padded), not Base64Url.
        // The credentials are untrusted input, so a malformed header is a failed authentication per RFC 6749 §5.2.
        using var base64Lease = TryRentDecodedCredentials(
            encodedCredentials.Span,
            out var base64Bytes
        );
        if (base64Lease is null)
            return CreateInvalidClientResult(openIdContext);

        using var decodeLease = SecureEncoding.UTF8.RentChars(
            base64Bytes,
            isSensitive: true,
            out Memory<char> decodeBuffer
        );

        return await AuthenticateDecodedCredentialsAsync(
            openIdContext,
            decodeBuffer,
            cancellationToken
        );
    }

    /// <summary>
    /// Decodes the standard Base64 text of the Basic credentials into a sensitive buffer.
    /// </summary>
    /// <param name="encodedCredentials">The Base64 text after the <c>Basic</c> scheme.</param>
    /// <param name="credentialBytes">The decoded bytes, valid until the returned owner is disposed.</param>
    /// <returns>
    /// The owner of <paramref name="credentialBytes"/>, which the caller disposes, or <see langword="null"/> when the
    /// text is not valid Base64.
    /// </returns>
    private static IMemoryOwner<byte>? TryRentDecodedCredentials(
        ReadOnlySpan<char> encodedCredentials,
        out Span<byte> credentialBytes
    )
    {
        try
        {
            return BinaryTextEncoding.RentDecoded(
                encodedCredentials,
                BinaryTextFormat.Base64,
                isSensitive: true,
                out credentialBytes
            );
        }
        catch (FormatException)
        {
            credentialBytes = default;
            return null;
        }
    }

    /// <summary>
    /// Authenticates the client from its decoded <c>user-id:password</c> text.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the request.</param>
    /// <param name="decodeBuffer">
    /// The sensitive <c>user-id:password</c> text. The caller owns its lease, so it is valid only until the caller
    /// disposes it.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that cancels the authentication.</param>
    /// <returns>The <see cref="ClientAuthenticationResult"/> of the authentication.</returns>
    private async ValueTask<ClientAuthenticationResult> AuthenticateDecodedCredentialsAsync(
        OpenIdContext openIdContext,
        Memory<char> decodeBuffer,
        CancellationToken cancellationToken
    )
    {
        var decodeSpan = decodeBuffer.Span;

        var indexOfColon = decodeSpan.IndexOf(':');

        // RFC 7617 §2: the credentials are the user-id and the password separated by a colon, so credentials without
        // one carry no client_secret and are a failed authentication.
        if (indexOfColon == -1)
            return CreateInvalidClientResult(openIdContext);

        var encodedClientId = decodeSpan[..indexOfColon];
        var clientId = UriDecode(encodedClientId);

        var encodedClientSecret = decodeBuffer[(indexOfColon + 1)..];

        using var secretLease = UriDecodeSensitive(encodedClientSecret, out var clientSecret);

        return await AuthenticateClientSecretAsync(
            openIdContext,
            clientId,
            clientSecret,
            cancellationToken
        );
    }
}
