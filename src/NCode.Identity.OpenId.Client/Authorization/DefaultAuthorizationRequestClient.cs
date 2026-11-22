// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Client.Http;
using NCode.Identity.OpenId.Client.ProofOfPossession;
using NCode.Identity.OpenId.Client.Tokens;

namespace NCode.Identity.OpenId.Client.Authorization;

/// <summary>
/// Provides a default implementation of <see cref="IAuthorizationRequestClient"/> that pushes the request when the
/// provider advertises a pushed authorization request endpoint (RFC 9126) and always uses PKCE <c>S256</c> (RFC 7636).
/// When the client uses DPoP, it also sends the <c>dpop_jkt</c> parameter (RFC 9449 §10) so the
/// authorization code is bound to the DPoP key.
/// </summary>
// See ADR-1275.
internal class DefaultAuthorizationRequestClient(
    IProviderMetadataResolver metadataResolver,
    ClientRequestSender sender,
    DPoPKeyResolver dpopKeyResolver
) : IAuthorizationRequestClient
{
    private IProviderMetadataResolver MetadataResolver { get; } = metadataResolver;

    private ClientRequestSender Sender { get; } = sender;

    private DPoPKeyResolver DPoPKeyResolver { get; } = dpopKeyResolver;

    /// <inheritdoc />
    public async ValueTask<AuthorizationRequestResult> CreateAuthorizationUriAsync(
        ClientRegistration registration,
        AuthorizationRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(request);

        var metadata = await MetadataResolver.GetMetadataAsync(
            registration.Authority,
            cancellationToken
        );

        var authorizationEndpoint = metadata.AuthorizationEndpoint;
        if (
            authorizationEndpoint is null
            || !ProviderUri.TryParseEndpoint(authorizationEndpoint, out _)
        )
        {
            throw new OpenIdProviderException(
                $"The provider '{registration.Authority}' has no acceptable authorization endpoint (https, or http on loopback)."
            );
        }

        if (
            metadata.CodeChallengeMethodsSupported is { Count: > 0 } methods
            && !methods.Contains(OpenIdProtocol.CodeChallengeMethods.S256, StringComparer.Ordinal)
        )
        {
            throw new OpenIdProviderException(
                $"The provider '{registration.Authority}' does not support the PKCE 'S256' code challenge method."
            );
        }

        var dpopJkt = DPoPKeyResolver
            .Resolve(registration, metadata, jwkThumbprint: null)
            ?.JwkThumbprint;

        var parEndpoint = metadata.PushedAuthorizationRequestEndpoint;
        if (string.IsNullOrEmpty(parEndpoint))
        {
            if (
                metadata.RequirePushedAuthorizationRequests
                || registration.RequirePushedAuthorizationRequests
            )
            {
                throw new OpenIdProviderException(
                    $"Pushed authorization requests are required for the client '{registration.ClientId}', but the provider '{registration.Authority}' advertises no pushed_authorization_request_endpoint."
                );
            }

            var authorizationParameters = BuildParameters(request, registration.ClientId, dpopJkt);
            var authorizationQueryUrl = AddQuery(authorizationEndpoint, authorizationParameters);
            var authorizationQueryUri = new Uri(authorizationQueryUrl);
            return new AuthorizationRequestResult(authorizationQueryUri)
            {
                DPoPJwkThumbprint = dpopJkt,
            };
        }

        var pushedEndpoint = new ProviderEndpoint(
            OpenIdProtocol.MetadataNames.PushedAuthorizationRequestEndpoint,
            parEndpoint
        );
        var pushedParameters = BuildParameters(request, clientId: null, dpopJkt);
        var response = await Sender.PostFormAsync(
            registration,
            metadata,
            pushedEndpoint,
            pushedParameters,
            includeClientId: true,
            cancellationToken
        );

        if (
            response.StatusCode == ProviderStatusCodes.Status201Created
            && response.Json is { ValueKind: JsonValueKind.Object } json
        )
        {
            if (
                !json.TryGetProperty(
                    OpenIdProtocol.Parameters.RequestUri,
                    out var requestUriElement
                )
                || requestUriElement.ValueKind != JsonValueKind.String
                || requestUriElement.GetString() is not { Length: > 0 } requestUri
                || !json.TryGetPropertyWholeInt32(
                    OpenIdProtocol.Parameters.ExpiresIn,
                    out var expiresIn
                )
                || expiresIn <= 0
            )
            {
                throw new OpenIdProviderException(
                    "The pushed authorization request endpoint answered without a request_uri or a positive expires_in."
                );
            }

            var clientIdParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ClientId,
                registration.ClientId
            );
            var requestUriParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.RequestUri,
                requestUri
            );
            var authorizationUri = AddQuery(
                authorizationEndpoint,
                [clientIdParameter, requestUriParameter]
            );

            var requestResultUri = new Uri(authorizationUri);
            return new AuthorizationRequestResult(requestResultUri, requestUri, expiresIn)
            {
                DPoPJwkThumbprint = dpopJkt,
            };
        }

        if (ProtocolErrorReader.TryRead(response, out var error))
            return new AuthorizationRequestResult(error);

        throw new OpenIdProviderException(
            $"The pushed authorization request endpoint answered HTTP {response.StatusCode} without an OAuth error."
        );
    }

    private static List<KeyValuePair<string, string>> BuildParameters(
        AuthorizationRequest request,
        string? clientId,
        string? dpopJkt
    )
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new(OpenIdProtocol.Parameters.ResponseType, OpenIdProtocol.ResponseTypes.Code),
        };

        if (clientId is not null)
        {
            var clientIdParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ClientId,
                clientId
            );
            parameters.Add(clientIdParameter);
        }

        var redirectUriParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.RedirectUri,
            request.RedirectUri
        );
        parameters.Add(redirectUriParameter);
        var stateParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.State,
            request.State
        );
        parameters.Add(stateParameter);
        var codeChallenge = ComputeS256Challenge(request.CodeVerifier);
        var codeChallengeParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.CodeChallenge,
            codeChallenge
        );
        parameters.Add(codeChallengeParameter);
        var codeChallengeMethodParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.CodeChallengeMethod,
            OpenIdProtocol.CodeChallengeMethods.S256
        );
        parameters.Add(codeChallengeMethodParameter);

        if (!string.IsNullOrEmpty(request.Nonce))
        {
            var nonceParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.Nonce,
                request.Nonce
            );
            parameters.Add(nonceParameter);
        }

        if (!string.IsNullOrEmpty(request.Scope))
        {
            var scopeParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.Scope,
                request.Scope
            );
            parameters.Add(scopeParameter);
        }

        if (!string.IsNullOrEmpty(request.ResponseMode))
        {
            var responseModeParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ResponseMode,
                request.ResponseMode
            );
            parameters.Add(responseModeParameter);
        }

        foreach (var resource in request.Resources)
        {
            var resourceParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.Resource,
                resource
            );
            parameters.Add(resourceParameter);
        }

        if (dpopJkt is not null)
        {
            var dpopJwkThumbprintParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.DPoPJwkThumbprint,
                dpopJkt
            );
            parameters.Add(dpopJwkThumbprintParameter);
        }

        if (request.AdditionalParameters is { } additionalParameters)
        {
            foreach (var (name, value) in additionalParameters)
            {
                var additionalParameter = new KeyValuePair<string, string>(name, value);
                parameters.Add(additionalParameter);
            }
        }

        return parameters;
    }

    private static string ComputeS256Challenge(string verifier)
    {
        using var verifierLease = SecureEncoding.ASCII.RentBytes(
            verifier,
            isSensitive: true,
            out Span<byte> verifierBytes
        );
        var hash = SHA256.HashData(verifierBytes);
        return Base64Url.EncodeToString(hash);
    }

    private static string AddQuery(string endpoint, List<KeyValuePair<string, string>> parameters)
    {
        var builder = new StringBuilder(endpoint);

        // ProviderUri.TryParseEndpoint rejects a fragment, so the query is appended at the end.
        var separator = endpoint.Contains('?') ? '&' : '?';
        foreach (var (name, value) in parameters)
        {
            builder.Append(separator);
            var escapedName = Uri.EscapeDataString(name);
            builder.Append(escapedName).Append('=');
            var escapedValue = Uri.EscapeDataString(value);
            builder.Append(escapedValue);
            separator = '&';
        }

        return builder.ToString();
    }
}
