// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default <see cref="ICellTokenSource"/>, which requests a client-credentials token from the directory's
/// own token endpoint, <c>directory/v1/token</c>, as the cell's machine client with a <c>private_key_jwt</c> assertion,
/// over the directory's own HTTP client, which trusts only <see cref="DirectoryClientOptions.CaCertificatesPath"/> when
/// it is set, and keeps the token until shortly before it expires. Concurrent callers share one request. The request
/// runs on its own time limit, <see cref="DirectoryClientOptions.TotalTimeout"/>, and each caller stops waiting with
/// its own <see cref="CancellationToken"/>, so a caller that gives up does not end the request for the others. A failed
/// request is remembered for <see cref="DirectoryClientOptions.TokenFailureCacheDuration"/>, during which a caller
/// fails at once, without a request to the directory.
/// </summary>
// See ADR-1285 and ADR-1315.
internal sealed class DefaultCellTokenSource : ICellTokenSource
{
    private static TimeSpan RefreshBeforeExpiry { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates the exception for a token endpoint answer that is not a success, mapping <c>invalid_client</c> to
    /// <see cref="DirectoryClientNotRegisteredException"/>.
    /// </summary>
    /// <param name="status">The status code of the answer.</param>
    /// <param name="body">The body of the answer.</param>
    /// <returns>The exception to throw.</returns>
    private static DirectoryUnavailableException CreateRefusal(
        HttpStatusCode status,
        ReadOnlySpan<byte> body
    )
    {
        var error = ReadError(body);
        if (string.Equals(error, OpenIdProtocol.ErrorCodes.InvalidClient, StringComparison.Ordinal))
            return new DirectoryClientNotRegisteredException(
                "The directory refused the cell's client authentication with 'invalid_client'."
            );

        return new DirectoryUnavailableException(
            $"The directory refused the token request with {(int)status} '{error ?? "unknown"}'."
        );
    }

    /// <summary>
    /// Reads the <c>error</c> member of an RFC 6749 section 5.2 error document.
    /// </summary>
    /// <param name="body">The body of the answer.</param>
    /// <returns>The error code, or <see langword="null"/> when the body holds none.</returns>
    private static string? ReadError(ReadOnlySpan<byte> body)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            return
                json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty(OpenIdProtocol.Parameters.ErrorCode, out var error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IHttpClientFactory HttpClientFactory { get; }

    private IClientAuthenticator? Authenticator { get; }

    private DirectoryClientOptions Options { get; }

    private TimeProvider TimeProvider { get; }

    private OpenIdProviderMetadata Metadata { get; }

    private ClientRegistration Registration { get; }

    private Uri TokenEndpoint { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultCellTokenSource"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The factory of the directory's own HTTP client, which trusts only
    /// <see cref="DirectoryClientOptions.CaCertificatesPath"/> when it is set.</param>
    /// <param name="authenticators">The client authenticators of the client library.</param>
    /// <param name="options">The <see cref="DirectoryClientOptions"/> of the cell.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> that supplies the current time.</param>
    public DefaultCellTokenSource(
        IHttpClientFactory httpClientFactory,
        IEnumerable<IClientAuthenticator> authenticators,
        IOptions<DirectoryClientOptions> options,
        TimeProvider timeProvider
    )
    {
        HttpClientFactory = httpClientFactory;
        Options = options.Value;
        TimeProvider = timeProvider;

        Authenticator = authenticators.FirstOrDefault(static authenticator =>
            string.Equals(
                authenticator.MethodName,
                OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
                StringComparison.Ordinal
            )
        );

        var issuer = (Options.Url ?? string.Empty).TrimEnd('/');
        var tokenEndpoint = $"{issuer}/{DirectoryClientDefaults.ApiPrefix}token";
        TokenEndpoint = new Uri(tokenEndpoint, UriKind.Absolute);
        Metadata = new OpenIdProviderMetadata
        {
            Issuer = issuer,
            TokenEndpoint = tokenEndpoint,
            TokenEndpointAuthMethodsSupported =
            [
                OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            ],
            TokenEndpointAuthSigningAlgValuesSupported =
            [
                .. GlobalDirectoryProtocol.ClientAssertionSigningAlgorithms,
            ],
        };
        Registration = new ClientRegistration(
            issuer,
            Options.ClientId ?? string.Empty,
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt
        )
        {
            KeyRing = Options.KeyRing,
        };
    }

    /// <summary>
    /// Gets the token that the cell stops using at <see cref="CachedExpiresAt"/>.
    /// </summary>
    private string? CachedToken { get; set; }

    /// <summary>
    /// Gets the time at which the cell stops using <see cref="CachedToken"/>.
    /// </summary>
    private DateTimeOffset CachedExpiresAt { get; set; }

    /// <summary>
    /// Gets the time at which the cell stops remembering that the request failed.
    /// </summary>
    private DateTimeOffset FailureRememberedUntil { get; set; }

    /// <summary>
    /// Gets the request that is running, which every caller waits on and which ends when it completes.
    /// </summary>
    private Lazy<Task<(string Token, DateTimeOffset ExpiresAt)>>? InFlight { get; set; }

    private Lock Gate { get; } = new();

    /// <inheritdoc />
    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        Lazy<Task<(string Token, DateTimeOffset ExpiresAt)>> flight;
        lock (Gate)
        {
            if (TryGetCached(out var cached))
                return cached;

            ThrowIfFailureRemembered();

            // A caller whose token is already cancelled starts no request.
            cancellationToken.ThrowIfCancellationRequested();

            flight = InFlight ??= new Lazy<Task<(string Token, DateTimeOffset ExpiresAt)>>(() =>
                RunFlightAsync().AsTask()
            );
        }

        // The caller stops waiting with its own token, and the request never runs on it.
        var entry = await flight.Value.WaitAsync(cancellationToken);
        return entry.Token;
    }

    /// <summary>
    /// Gets the cached token when the cell still uses it. The caller holds <see cref="Gate"/>.
    /// </summary>
    /// <param name="token">The cached token when this method returns <see langword="true"/>; otherwise,
    /// <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when an unexpired token is cached; otherwise, <see langword="false"/>.</returns>
    private bool TryGetCached([NotNullWhen(true)] out string? token)
    {
        if (CachedToken is { } cached && CachedExpiresAt > TimeProvider.GetUtcNow())
        {
            token = cached;
            return true;
        }

        token = null;
        return false;
    }

    /// <summary>
    /// Throws when the cell remembers that the request failed, and the memory has not ended. The caller holds
    /// <see cref="Gate"/>.
    /// </summary>
    /// <exception cref="DirectoryUnavailableException">The request failed moments ago.</exception>
    private void ThrowIfFailureRemembered()
    {
        if (FailureRememberedUntil > TimeProvider.GetUtcNow())
            throw new DirectoryUnavailableException(
                "The directory token request failed moments ago."
            );
    }

    /// <summary>
    /// Requests the token for every caller that waits on it, and clears the running request when it ends. The request
    /// runs on a time limit of its own, never on a caller's token. It checks the cache and the remembered failure
    /// first, because another request may have ended since its callers looked.
    /// </summary>
    /// <returns>The access token and the time at which the cell stops using it, before the token expires.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell remembers that the last request failed, or the request fails or outlasts its time limit.
    /// </exception>
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The shared request runs on its own bounded token, so no caller's token reaches it (ADR-1315)."
    )]
    private async ValueTask<(string Token, DateTimeOffset ExpiresAt)> RunFlightAsync()
    {
        try
        {
            lock (Gate)
            {
                if (TryGetCached(out var cached))
                    return (cached, CachedExpiresAt);

                ThrowIfFailureRemembered();
            }

            try
            {
                using var source = new CancellationTokenSource(Options.TotalTimeout, TimeProvider);
                var entry = await RequestTokenAsync(source.Token);
                lock (Gate)
                {
                    CachedToken = entry.Token;
                    CachedExpiresAt = entry.ExpiresAt;
                    FailureRememberedUntil = default;
                }

                return entry;
            }
            catch (OperationCanceledException exception)
            {
                RememberFailure();
                throw new DirectoryUnavailableException(
                    "The directory token request was cancelled or timed out.",
                    exception
                );
            }
            catch (DirectoryUnavailableException)
            {
                RememberFailure();
                throw;
            }
        }
        finally
        {
            lock (Gate)
                InFlight = null;
        }
    }

    /// <summary>
    /// Remembers that the request failed, for <see cref="DirectoryClientOptions.TokenFailureCacheDuration"/>.
    /// </summary>
    private void RememberFailure()
    {
        lock (Gate)
            FailureRememberedUntil = TimeProvider.GetUtcNow() + Options.TokenFailureCacheDuration;
    }

    /// <summary>
    /// Requests a client-credentials access token from the directory's token endpoint, authenticating the cell with its
    /// private key.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The access token and the time at which the cell stops using it, before the token expires.</returns>
    /// <exception cref="DirectoryClientNotRegisteredException">
    /// The directory refuses the cell's client authentication.
    /// </exception>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell is not configured to authenticate, the token endpoint cannot be reached, or the directory refuses the
    /// request or answers without an access token or without a positive <c>expires_in</c> lifetime.
    /// </exception>
    private async ValueTask<(string Token, DateTimeOffset ExpiresAt)> RequestTokenAsync(
        CancellationToken cancellationToken
    )
    {
        if (
            Authenticator is null
            || string.IsNullOrEmpty(Options.ClientId)
            || string.IsNullOrEmpty(Options.KeyRing)
        )
            throw new DirectoryUnavailableException(
                "The cell is not configured to authenticate to the directory."
            );

        var parameters = new List<KeyValuePair<string, string>>
        {
            new(OpenIdProtocol.Parameters.GrantType, OpenIdProtocol.GrantTypes.ClientCredentials),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        var acceptJson = new MediaTypeWithQualityHeaderValue(OpenIdProtocol.MediaTypes.Json);
        request.Headers.Accept.Add(acceptJson);

        var context = new ClientAuthenticationContext(
            Registration,
            Metadata,
            TokenEndpoint.AbsoluteUri,
            request,
            parameters
        );
        try
        {
            await Authenticator.AuthenticateAsync(context, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new DirectoryUnavailableException(
                "The cell cannot sign its client assertion for the directory.",
                exception
            );
        }

        request.Content = new FormUrlEncodedContent(parameters);

        using var body = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
        HttpStatusCode status;
        try
        {
            using var client = HttpClientFactory.CreateClient(
                DirectoryClientDefaults.HttpClientName
            );
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            status = response.StatusCode;
            await DirectoryResponseReader.ReadAsync(
                response.Content,
                body,
                DirectoryClientDefaults.MaxTokenResponseBytes,
                TokenEndpoint.AbsoluteUri,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new DirectoryUnavailableException(
                $"The directory's token endpoint could not be reached: {TokenEndpoint}.",
                exception
            );
        }

        if (status != HttpStatusCode.OK)
            throw CreateRefusal(status, body.WrittenSpan);

        TokenResponse? tokenResponse;
        try
        {
            tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body.WrittenSpan);
        }
        catch (JsonException exception)
        {
            throw new DirectoryUnavailableException(
                "The directory answered the token request with a document that is not a token response.",
                exception
            );
        }

        var accessToken = tokenResponse?.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
            throw new DirectoryUnavailableException(
                "The directory answered the token request without an access token."
            );

        var expiresIn = tokenResponse?.ExpiresIn;
        if (expiresIn is not > 0)
            throw new DirectoryUnavailableException(
                "The directory answered the token request without a positive expires_in."
            );

        var lifetime = TimeSpan.FromSeconds(expiresIn.Value);
        var expiresAt = TimeProvider.GetUtcNow() + lifetime - RefreshBeforeExpiry;
        return (accessToken, expiresAt);
    }
}
