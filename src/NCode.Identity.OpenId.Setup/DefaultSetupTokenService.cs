// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Setup.Logging;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Provides a default implementation of <see cref="ISetupTokenService"/>. A configured <see cref="SetupOptions.Token"/>
/// wins: only its SHA-256 hash is held, and it is never logged. Without one, an uninitialized instance generates a
/// 256-bit token, stores only its SHA-256 hash with its issue and expiry instants in the instance setup row that every
/// replica reads, and logs the token once at Warning on the replica that stored it.
/// </summary>
internal sealed class DefaultSetupTokenService(
    IStoreManagerFactory storeManagerFactory,
    IOptions<SetupOptions> options,
    TimeProvider timeProvider,
    SetupProcessClock processClock,
    ILogger<DefaultSetupTokenService> logger
) : ISetupTokenService
{
    /// <summary>
    /// Specifies the number of random bytes in a generated setup token.
    /// </summary>
    internal const int GeneratedTokenByteLength = 32;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private SetupProcessClock ProcessClock { get; } = processClock;

    private ILogger<DefaultSetupTokenService> Logger { get; } = logger;

    // The options are read on first use, so resolving the service never reads or validates them.
    private Lazy<(TimeSpan GeneratedTokenLifetime, byte[]? ConfiguredTokenHash)> Resolved { get; } =
        new(() => ResolveOptions(options.Value), LazyThreadSafetyMode.ExecutionAndPublication);

    private TimeSpan GeneratedTokenLifetime => Resolved.Value.GeneratedTokenLifetime;

    private byte[]? ConfiguredTokenHash => Resolved.Value.ConfiguredTokenHash;

    /// <inheritdoc />
    public string TokenSource =>
        ConfiguredTokenHash is null ? SetupTokenSources.Generated : SetupTokenSources.Configured;

    private static (TimeSpan GeneratedTokenLifetime, byte[]? ConfiguredTokenHash) ResolveOptions(
        SetupOptions setupOptions
    ) => (setupOptions.GeneratedTokenLifetime, HashConfiguredToken(setupOptions.Token));

    private static byte[]? HashConfiguredToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : HashToken(token);

    /// <summary>
    /// Hashes a setup token with SHA-256.
    /// </summary>
    /// <param name="token">The token to hash; its UTF-8 bytes are rented as sensitive and zeroed.</param>
    /// <returns>The 32-byte SHA-256 hash of the token's UTF-8 bytes.</returns>
    private static byte[] HashToken(string token)
    {
        using var owner = SecureEncoding.UTF8.RentBytes(
            token,
            isSensitive: true,
            out Span<byte> bytes
        );
        return SHA256.HashData(bytes);
    }

    /// <summary>
    /// Generates a random setup token.
    /// </summary>
    /// <returns>The base64url text, without padding, of <see cref="GeneratedTokenByteLength"/> random bytes, which are
    /// rented as sensitive and zeroed.</returns>
    private static string GenerateToken()
    {
        using var owner = BufferFactory.RentRandom(
            GeneratedTokenByteLength,
            isSensitive: true,
            out Span<byte> bytes
        );
        return Base64Url.EncodeToString(bytes);
    }

    /// <inheritdoc />
    public async ValueTask EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (ConfiguredTokenHash is not null)
            return;

        var setup = await InstanceSetupRecords.GetOrAddAsync(
            StoreManagerFactory,
            cancellationToken
        );
        if (setup.IsInitialized)
            return;

        // A token issued at or after this process started is this process's or that of a replica of the same rollout.
        if (setup.TokenIssuedAt is { } storedIssuedAt && storedIssuedAt >= ProcessClock.StartedAt)
            return;

        var token = GenerateToken();
        var issuedAt = TimeProvider.GetUtcNow();
        var expiresAt = issuedAt + GeneratedTokenLifetime;

        setup.TokenHash = HashToken(token);
        setup.TokenIssuedAt = issuedAt;
        setup.TokenExpiresAt = expiresAt;

        try
        {
            await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
            var store = storeManager.GetStore<IInstanceSetupStore>();

            await store.UpdateAsync(setup, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
        }
        catch (StoreConcurrencyException)
        {
            // Another replica stored a token between the read above and this write, so that replica logs its token.
            var concurrent = await InstanceSetupRecords.GetOrAddAsync(
                StoreManagerFactory,
                cancellationToken
            );
            Logger.SetupTokenIssuedByAnotherReplica(concurrent.TokenIssuedAt);
            return;
        }

        Logger.SetupTokenIssued(token, expiresAt);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var presentedHash = HashToken(token);

        if (ConfiguredTokenHash is not null)
            return CryptographicOperations.FixedTimeEquals(presentedHash, ConfiguredTokenHash);

        var setup = await FindAsync(cancellationToken);
        if (setup?.TokenHash is not { } storedHash)
            return false;

        var isMatch = CryptographicOperations.FixedTimeEquals(presentedHash, storedHash);

        return isMatch
            && !setup.IsInitialized
            && setup.TokenExpiresAt is { } expiresAt
            && expiresAt > TimeProvider.GetUtcNow();
    }

    private async ValueTask<PersistedInstanceSetup?> FindAsync(CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IInstanceSetupStore>();
        return await store.GetOrDefaultAsync(cancellationToken);
    }
}
