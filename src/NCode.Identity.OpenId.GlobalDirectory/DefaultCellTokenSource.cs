// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default <see cref="ICellTokenSource"/>, which requests a client-credentials token through
/// <see cref="ITokenClient"/> as the cell's machine client and keeps one token for each audience and scope until
/// shortly before it expires. Each audience and scope has its own single flight: concurrent callers for one audience
/// and scope share one request, and a request for one audience and scope never waits on a request for another.
/// </summary>
// See ADR-1285.
internal sealed class DefaultCellTokenSource(
    ITokenClient tokenClient,
    IOptions<DirectoryClientOptions> options,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider
) : ICellTokenSource, IDisposable
{
    private static TimeSpan RefreshBeforeExpiry { get; } = TimeSpan.FromSeconds(30);

    private ITokenClient TokenClient { get; } = tokenClient;

    private DirectoryClientOptions Options { get; } = options.Value;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Gets the gate of each audience and scope, which lets one caller at a time request that audience and scope's
    /// token.
    /// </summary>
    private ConcurrentDictionary<(string Audience, string Scope), SemaphoreSlim> Gates { get; } =
        new();

    /// <summary>
    /// Gets the token of each audience and scope, with the time at which the cell stops using it.
    /// </summary>
    private ConcurrentDictionary<
        (string Audience, string Scope),
        (string Token, DateTimeOffset ExpiresAt)
    > Cache { get; } = new();

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
        if (TryGetCachedToken(key, out var token))
            return token;

        var gate = Gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedToken(key, out token))
                return token;

            var entry = await RequestTokenAsync(audience, scope, cancellationToken);
            Cache[key] = entry;
            return entry.Token;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gets the cached token of an audience and scope when the cell still uses it.
    /// </summary>
    /// <param name="key">The audience and scope of the token.</param>
    /// <param name="token">The cached token when this method returns <see langword="true"/>; otherwise, an empty string.</param>
    /// <returns><see langword="true"/> when an unexpired token is cached; otherwise, <see langword="false"/>.</returns>
    private bool TryGetCachedToken((string Audience, string Scope) key, out string token)
    {
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAt > TimeProvider.GetUtcNow())
        {
            token = cached.Token;
            return true;
        }

        token = string.Empty;
        return false;
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
    /// the request or answers without an access token.
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

        var lifetime = TimeSpan.FromSeconds(result.Response.ExpiresIn ?? 0);
        var expiresAt = TimeProvider.GetUtcNow() + lifetime - RefreshBeforeExpiry;
        return (accessToken, expiresAt);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in Gates.Values)
            gate.Dispose();
    }
}
