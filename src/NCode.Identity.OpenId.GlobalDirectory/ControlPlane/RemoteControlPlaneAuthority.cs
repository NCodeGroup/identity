// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IControlPlaneAuthority"/> of a cell that is not the control plane. It posts the question to
/// the control-plane cell's <c>control-plane/v1/authority</c> endpoint, at the origin of
/// <c>Cell:ControlPlane:Issuer</c>, with a client-credentials token for the audience
/// <see cref="GlobalDirectoryProtocol.ControlPlaneAudience"/>.
/// It fails closed: a missing issuer, a failed call or an answer that is not a <see cref="ControlPlaneAuthority"/> is a
/// <see cref="DirectoryUnavailableException"/>, never an empty grant. A successful answer is kept for 30 seconds in a
/// bounded in-process cache; a failure is never kept.
/// </summary>
// See ADR-1411.
internal sealed class RemoteControlPlaneAuthority(
    DirectoryHttpClient client,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider
) : IControlPlaneAuthority
{
    private const int MaxCacheEntries = 1024;

    private static TimeSpan CacheLifetime { get; } = TimeSpan.FromSeconds(30);

    private DirectoryHttpClient Client { get; } = client;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ConcurrentDictionary<
        (string Subject, string ClientId, string? TeamId),
        (ControlPlaneAuthority Authority, DateTimeOffset ExpiresAt)
    > Cache { get; } = [];

    private Lock StoreLock { get; } = new();

    /// <inheritdoc />
    public async ValueTask<ControlPlaneAuthority> GetAsync(
        ControlPlaneAuthorityRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = (request.Subject, request.ClientId, request.TeamId);
        var now = TimeProvider.GetUtcNow();
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
            return cached.Authority;

        var response = await Client.SendAsync(
            new CellApiRequest(
                HttpMethod.Post,
                CreateAuthorityUri(),
                GlobalDirectoryProtocol.ControlPlaneAudience,
                GlobalDirectoryProtocol.ControlPlaneScope
            )
            {
                Body = request,
            },
            cancellationToken
        );

        response.EnsureSuccess("get the control-plane authority");
        var authority = response.Read<ControlPlaneAuthority>();

        Store(key, authority, TimeProvider.GetUtcNow() + CacheLifetime);
        return authority;
    }

    private Uri CreateAuthorityUri()
    {
        var issuer = CellOptions.ControlPlane.Issuer;
        if (string.IsNullOrEmpty(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
            throw new DirectoryUnavailableException(
                "The cell does not know the issuer of the control plane."
            );

        return new Uri(
            uri.GetLeftPart(UriPartial.Authority) + DirectoryClientDefaults.AuthorityPath
        );
    }

    private void Store(
        (string Subject, string ClientId, string? TeamId) key,
        ControlPlaneAuthority authority,
        DateTimeOffset expiresAt
    )
    {
        lock (StoreLock)
        {
            if (Cache.Count >= MaxCacheEntries && !Cache.ContainsKey(key))
            {
                var now = TimeProvider.GetUtcNow();
                var removedAny = false;
                foreach (var entry in Cache)
                {
                    if (entry.Value.ExpiresAt <= now && Cache.TryRemove(entry))
                        removedAny = true;
                }

                if (!removedAny)
                    Cache.Clear();
            }

            Cache[key] = (authority, expiresAt);
        }
    }
}
