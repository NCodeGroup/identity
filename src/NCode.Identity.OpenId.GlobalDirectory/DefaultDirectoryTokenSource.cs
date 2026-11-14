// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default <see cref="IDirectoryTokenSource"/>, which requests a client-credentials token through
/// <see cref="ITokenClient"/> as the cell's machine client and keeps it until shortly before it expires. Concurrent
/// callers share one request.
/// </summary>
// See ADR-1285.
internal sealed class DefaultDirectoryTokenSource(
    ITokenClient tokenClient,
    IOptions<DirectoryClientOptions> options,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider
) : IDirectoryTokenSource, IDisposable
{
    private static TimeSpan RefreshBeforeExpiry { get; } = TimeSpan.FromSeconds(30);

    private ITokenClient TokenClient { get; } = tokenClient;

    private DirectoryClientOptions Options { get; } = options.Value;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private SemaphoreSlim Gate { get; } = new(1, 1);

    private string? CachedToken { get; set; }

    private DateTimeOffset CachedExpiresAt { get; set; }

    /// <inheritdoc />
    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (CachedToken is { } cached && CachedExpiresAt > TimeProvider.GetUtcNow())
                return cached;

            var (token, expiresAt) = await RequestTokenAsync(cancellationToken);
            CachedToken = token;
            CachedExpiresAt = expiresAt;
            return token;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async ValueTask<(string Token, DateTimeOffset ExpiresAt)> RequestTokenAsync(
        CancellationToken cancellationToken
    )
    {
        var authority = CellOptions.ControlPlane.Issuer;
        var clientId = Options.ClientId;
        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId))
            throw new DirectoryUnavailableException(
                "The cell is not configured to authenticate to the directory."
            );

        var registration = new ClientRegistration(
            authority,
            clientId,
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt
        )
        {
            KeyRing = Options.KeyRing,
        };

        var request = new ClientCredentialsTokenRequest
        {
            Scope = DirectoryClientDefaults.Scope,
            Resources = [DirectoryClientDefaults.Audience],
        };

        TokenResult result;
        try
        {
            result = await TokenClient.RequestTokenAsync(registration, request, cancellationToken);
        }
        catch (OpenIdProviderException exception)
        {
            throw new DirectoryUnavailableException(
                "The token endpoint of the control plane could not be reached.",
                exception
            );
        }

        if (!result.IsSuccess)
        {
            throw new DirectoryUnavailableException(
                $"The control plane refused the directory token request with '{result.Error.Error}'."
            );
        }

        var accessToken = result.Response.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
            throw new DirectoryUnavailableException(
                "The control plane answered the directory token request without an access token."
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
