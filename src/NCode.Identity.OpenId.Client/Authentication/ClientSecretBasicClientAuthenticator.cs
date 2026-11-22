// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using NCode.Buffers;

namespace NCode.Identity.OpenId.Client.Authentication;

/// <summary>
/// Provides an implementation of <see cref="IClientAuthenticator"/> for the <c>client_secret_basic</c> method, which sends
/// the client's credentials in the HTTP <c>Authorization</c> header.
/// </summary>
internal class ClientSecretBasicClientAuthenticator(IClientKeyRingProvider keyRingProvider)
    : IClientAuthenticator
{
    private IClientKeyRingProvider KeyRingProvider { get; } = keyRingProvider;

    /// <inheritdoc />
    public string MethodName => OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic;

    /// <inheritdoc />
    public bool UsesMutualTls => false;

    /// <inheritdoc />
    public ValueTask AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        var registration = context.Registration;
        var secret = SharedSecretReader.ReadSecret(KeyRingProvider, registration);

        var encodedClientId = WebUtility.UrlEncode(registration.ClientId);
        var encodedSecret = WebUtility.UrlEncode(secret);

        using var text = new PooledTextBuilder(stackalloc char[256], isSensitive: true);
        text.Append(encodedClientId);
        text.Append(':');
        text.Append(encodedSecret);
        using var bytesLease = SecureEncoding.UTF8.RentBytes(
            text.WrittenSpan,
            isSensitive: true,
            out Span<byte> bytes
        );

        var base64Credentials = Convert.ToBase64String(bytes);
        context.Request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            base64Credentials
        );
        return ValueTask.CompletedTask;
    }
}
