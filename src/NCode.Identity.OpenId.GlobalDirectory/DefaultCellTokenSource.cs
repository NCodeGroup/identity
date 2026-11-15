// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default <see cref="ICellTokenSource"/>, which requests a client-credentials token through
/// <see cref="ITokenClient"/> as the cell's machine client and keeps one token for each audience and scope until
/// shortly before it expires. Each audience and scope has its own single flight: concurrent callers for one audience
/// and scope share one request, and a request for one audience and scope never waits on a request for another. The
/// request runs on its own time limit, <see cref="DirectoryClientOptions.TokenRequestTimeout"/>, and each caller stops
/// waiting with its own <see cref="CancellationToken"/>, so a caller that gives up does not end the request for the
/// others. A failed request is remembered for <see cref="DirectoryClientOptions.TokenFailureCacheDuration"/>, during
/// which a caller that needs that token fails at once, without a request to the control plane.
/// </summary>
// See ADR-1285 and ADR-1315.
internal sealed class DefaultCellTokenSource(
    ITokenClient tokenClient,
    IOptions<DirectoryClientOptions> options,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider
) : ICellTokenSource
{
    private static TimeSpan RefreshBeforeExpiry { get; } = TimeSpan.FromSeconds(30);

    private ITokenClient TokenClient { get; } = tokenClient;

    private DirectoryClientOptions Options { get; } = options.Value;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    // The three dictionaries below are keyed by audience and scope. The cell asks for the directory's and the control
    // plane's audience and scope pairs only, a fixed set, so none of them grows without bound.

    /// <summary>
    /// Gets the token of each audience and scope, with the time at which the cell stops using it.
    /// </summary>
    private ConcurrentDictionary<
        (string Audience, string Scope),
        (string Token, DateTimeOffset ExpiresAt)
    > Cache { get; } = new();

    /// <summary>
    /// Gets the request that is running for each audience and scope. Every caller of that audience and scope waits on
    /// it, and it leaves the dictionary when it ends.
    /// </summary>
    private ConcurrentDictionary<
        (string Audience, string Scope),
        Lazy<Task<(string Token, DateTimeOffset ExpiresAt)>>
    > InFlight { get; } = new();

    /// <summary>
    /// Gets the time at which the cell stops remembering that the request for each audience and scope failed.
    /// </summary>
    private ConcurrentDictionary<
        (string Audience, string Scope),
        DateTimeOffset
    > Failures { get; } = new();

    /// <inheritdoc />
    public async ValueTask<string> GetAccessTokenAsync(
        string audience,
        string scope,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(audience);
        ArgumentException.ThrowIfNullOrEmpty(scope);

        var key = (audience, scope);
        if (TryGetCachedEntry(key, out var cached))
            return cached.Token;

        ThrowIfFailureRemembered(key);

        // A caller whose token is already cancelled starts no request.
        cancellationToken.ThrowIfCancellationRequested();

        var flight = InFlight.GetOrAdd(
            key,
            k => new Lazy<Task<(string Token, DateTimeOffset ExpiresAt)>>(() =>
                RunFlightAsync(k).AsTask()
            )
        );

        // The caller stops waiting with its own token, and the request never runs on it.
        var entry = await flight.Value.WaitAsync(cancellationToken);
        return entry.Token;
    }

    /// <summary>
    /// Gets the cached token of an audience and scope when the cell still uses it.
    /// </summary>
    /// <param name="key">The audience and scope of the token.</param>
    /// <param name="entry">
    /// The cached token and the time at which the cell stops using it when this method returns
    /// <see langword="true"/>; otherwise, an empty token.
    /// </param>
    /// <returns><see langword="true"/> when an unexpired token is cached; otherwise, <see langword="false"/>.</returns>
    private bool TryGetCachedEntry(
        (string Audience, string Scope) key,
        out (string Token, DateTimeOffset ExpiresAt) entry
    )
    {
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAt > TimeProvider.GetUtcNow())
        {
            entry = cached;
            return true;
        }

        entry = (string.Empty, default);
        return false;
    }

    /// <summary>
    /// Throws when the cell remembers that the request for an audience and scope failed, and the memory has not ended.
    /// </summary>
    /// <param name="key">The audience and scope of the token.</param>
    /// <exception cref="DirectoryUnavailableException">
    /// The request for the audience and scope failed moments ago.
    /// </exception>
    private void ThrowIfFailureRemembered((string Audience, string Scope) key)
    {
        if (
            Failures.TryGetValue(key, out var rememberedUntil)
            && rememberedUntil > TimeProvider.GetUtcNow()
        )
            throw new DirectoryUnavailableException(
                $"The token request for '{key.Audience}' failed moments ago."
            );
    }

    /// <summary>
    /// Remembers that the request for an audience and scope failed, for
    /// <see cref="DirectoryClientOptions.TokenFailureCacheDuration"/>.
    /// </summary>
    /// <param name="key">The audience and scope of the token.</param>
    private void RememberFailure((string Audience, string Scope) key) =>
        Failures[key] = TimeProvider.GetUtcNow() + Options.TokenFailureCacheDuration;

    /// <summary>
    /// Requests the token of an audience and scope for every caller that waits on it, and leaves the dictionary of
    /// running requests when it ends. The request runs on a time limit of its own, never on a caller's token. It checks
    /// the cache and the remembered failure first, because another request may have ended since its callers looked.
    /// </summary>
    /// <param name="key">The audience and scope of the token.</param>
    /// <returns>The access token and the time at which the cell stops using it, before the token expires.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell remembers that the last request failed, or the request fails or outlasts its time limit.
    /// </exception>
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The shared request runs on its own bounded token, so no caller's token reaches it (ADR-1315)."
    )]
    private async ValueTask<(string Token, DateTimeOffset ExpiresAt)> RunFlightAsync(
        (string Audience, string Scope) key
    )
    {
        try
        {
            if (TryGetCachedEntry(key, out var cached))
                return cached;

            ThrowIfFailureRemembered(key);

            try
            {
                using var source = new CancellationTokenSource(
                    Options.TokenRequestTimeout,
                    TimeProvider
                );
                var entry = await RequestTokenAsync(key.Audience, key.Scope, source.Token);
                Cache[key] = entry;
                Failures.TryRemove(key, out _);
                return entry;
            }
            catch (OperationCanceledException exception)
            {
                RememberFailure(key);
                throw new DirectoryUnavailableException(
                    $"The token request for '{key.Audience}' was cancelled or timed out.",
                    exception
                );
            }
            catch (DirectoryUnavailableException)
            {
                RememberFailure(key);
                throw;
            }
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Requests a client-credentials access token from the control plane, authenticating the cell with its private key.
    /// </summary>
    /// <param name="audience">The resource that the token is for.</param>
    /// <param name="scope">The scope that the token carries.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The access token and the time at which the cell stops using it, before the token expires.</returns>
    /// <exception cref="DirectoryUnavailableException">
    /// The cell is not configured to authenticate, the token endpoint cannot be reached, or the control plane refuses
    /// the request or answers without an access token or without a positive <c>expires_in</c> lifetime.
    /// </exception>
    private async ValueTask<(string Token, DateTimeOffset ExpiresAt)> RequestTokenAsync(
        string audience,
        string scope,
        CancellationToken cancellationToken
    )
    {
        var authority = CellOptions.ControlPlane.Issuer;
        var clientId = Options.ClientId;
        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId))
            throw new DirectoryUnavailableException(
                $"The cell is not configured to authenticate to '{audience}'."
            );

        var registration = new ClientRegistration(
            authority,
            clientId,
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt
        )
        {
            KeyRing = Options.KeyRing,
        };

        var request = new ClientCredentialsTokenRequest { Scope = scope, Resources = [audience] };

        TokenResult result;
        try
        {
            result = await TokenClient.RequestTokenAsync(registration, request, cancellationToken);
        }
        catch (OpenIdProviderException exception)
        {
            throw new DirectoryUnavailableException(
                $"The token endpoint of the control plane could not be reached for '{audience}'.",
                exception
            );
        }

        if (!result.IsSuccess)
        {
            throw new DirectoryUnavailableException(
                $"The control plane refused the token request for '{audience}' with '{result.Error.Error}'."
            );
        }

        var accessToken = result.Response.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
            throw new DirectoryUnavailableException(
                $"The control plane answered the token request for '{audience}' without an access token."
            );

        var expiresIn = result.Response.ExpiresIn;
        if (expiresIn is not > 0)
            throw new DirectoryUnavailableException(
                $"The control plane answered the token request for '{audience}' without a positive expires_in."
            );

        var lifetime = TimeSpan.FromSeconds(expiresIn.Value);
        var expiresAt = TimeProvider.GetUtcNow() + lifetime - RefreshBeforeExpiry;
        return (accessToken, expiresAt);
    }
}
