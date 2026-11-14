// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default <see cref="ICellTokenSource"/>, which requests a client-credentials token through
/// <see cref="ITokenClient"/> as the cell's machine client and keeps one token for each audience and scope until
/// shortly before it expires. Concurrent callers share one request.
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

    private SemaphoreSlim Gate { get; } = new(1, 1);

    private Dictionary<
        (string Audience, string Scope),
        (string Token, DateTimeOffset ExpiresAt)
    > Cache { get; } = [];

    /// <inheritdoc />
    public async ValueTask<string> GetAccessTokenAsync(
        string audience,
        string scope,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(audience);
        ArgumentException.ThrowIfNullOrEmpty(scope);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var key = (audience, scope);
            if (
                Cache.TryGetValue(key, out var cached)
                && cached.ExpiresAt > TimeProvider.GetUtcNow()
            )
                return cached.Token;

            var entry = await RequestTokenAsync(audience, scope, cancellationToken);
            Cache[key] = entry;
            return entry.Token;
        }
        finally
        {
            Gate.Release();
        }
    }

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
        Gate.Dispose();
    }
}
