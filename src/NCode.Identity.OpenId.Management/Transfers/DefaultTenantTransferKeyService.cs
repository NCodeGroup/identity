// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NCode.Buffers;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyTypes;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Provides the default implementation of <see cref="ITenantTransferKeyService"/>. It stores each key's PKCS#8 bytes
/// under a data protector in the cell's <see cref="IExpiringValueSet"/>, so any replica of the cell can take the key
/// once, and it zeroes the private bytes after each use.
/// </summary>
// See ADR-1514.
internal sealed class DefaultTenantTransferKeyService(
    ISecretKeyFactory secretKeyFactory,
    ISecretKeyTypeCatalog secretKeyTypeCatalog,
    IDataProtectionProvider dataProtectionProvider,
    IExpiringValueSet expiringValueSet,
    TimeProvider timeProvider
) : ITenantTransferKeyService
{
    /// <summary>
    /// The purpose of the data protector that protects the stored private keys.
    /// </summary>
    internal const string ProtectorPurpose = "NCode.Identity.OpenId.Management.TransferKeys";

    /// <summary>
    /// The prefix of the name that each stored key has in the <see cref="IExpiringValueSet"/>.
    /// </summary>
    internal const string KeyPrefix = "tenant-transfer-key:";

    /// <summary>
    /// The <c>use</c> of a transfer key.
    /// </summary>
    internal const string KeyUse = "enc";

    /// <summary>
    /// The <c>alg</c> of a transfer key.
    /// </summary>
    internal const string KeyAlgorithm = "ECDH-ES+A256KW";

    private const int KeyIdSizeBytes = 16;

    /// <summary>
    /// The time that a transfer key lives before it expires.
    /// </summary>
    internal static TimeSpan Lifetime { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Creates the <c>kid</c> of a transfer key.
    /// </summary>
    /// <returns>The base64url text, without padding, of <see cref="KeyIdSizeBytes"/> random bytes, which are not
    /// sensitive because the key's public JWK publishes them.</returns>
    private static string CreateKeyId()
    {
        using var owner = BufferFactory.RentRandom(
            KeyIdSizeBytes,
            isSensitive: false,
            out Span<byte> bytes
        );
        return Base64Url.EncodeToString(bytes);
    }

    private ISecretKeyFactory SecretKeyFactory { get; } = secretKeyFactory;
    private ISecretKeyTypeCatalog SecretKeyTypeCatalog { get; } = secretKeyTypeCatalog;
    private IDataProtector Protector { get; } =
        dataProtectionProvider.CreateProtector(ProtectorPurpose);
    private IExpiringValueSet ExpiringValueSet { get; } = expiringValueSet;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public async ValueTask<TenantTransferKey> CreateAsync(CancellationToken cancellationToken)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = ecdh.ExportPkcs8PrivateKey();
        try
        {
            var keyId = CreateKeyId();
            var secretKey = SecretKeyFactory.CreateEccPkcs8(CreateMetadata(keyId), pkcs8);

            if (
                !SecretKeyTypeCatalog.TryGet(secretKey.KeyType, out var descriptor)
                || descriptor.ExportPublicKey(secretKey) is not { } exported
            )
            {
                throw new InvalidOperationException(
                    "The transfer key's type cannot export a public key."
                );
            }

            var added = await ExpiringValueSet.TryAddAsync(
                KeyPrefix + keyId,
                Protector.Protect(pkcs8),
                TimeProvider.GetUtcNow() + Lifetime,
                cancellationToken
            );

            if (!added)
            {
                throw new InvalidOperationException("A transfer key with the same kid exists.");
            }

            return new TenantTransferKey { KeyId = keyId, Jwk = exported.Jwk.AsElement() };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    /// <inheritdoc />
    public async ValueTask<SecretKey?> TakeAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyId);

        var taken = await ExpiringValueSet.TryTakeAsync(KeyPrefix + keyId, cancellationToken);
        if (taken is not { } value)
        {
            return null;
        }

        byte[]? pkcs8 = null;
        try
        {
            pkcs8 = Protector.Unprotect(value.Value.ToArray());
            return SecretKeyFactory.CreateEccPkcs8(CreateMetadata(keyId), pkcs8);
        }
        catch (CryptographicException)
        {
            // A value that the protector cannot read is no key; the take already removed it.
            return null;
        }
        finally
        {
            if (pkcs8 is not null)
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
        }
    }

    private static KeyMetadata CreateMetadata(string keyId) =>
        new()
        {
            KeyId = keyId,
            Use = KeyUse,
            Algorithm = KeyAlgorithm,
        };
}
