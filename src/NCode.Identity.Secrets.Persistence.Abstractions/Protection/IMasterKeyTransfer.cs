// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Moves the master keys of an owner from one cell to another as a package that only the holder of a recipient key can
/// read, so that the secrets the owner stored as ciphertext stay readable after the move.
/// </summary>
/// <remarks>
/// <para>
/// The package is a compact JWE to the recipient key (<c>ECDH-ES+A256KW</c> with <c>A256GCM</c>). It carries the raw
/// key material of every master key of the owner, active and retired, with its identifier, version and dates, and the
/// root key the owner brings, if any. The importing cell wraps each key again under the root of its own owner, so no
/// wrapped form of the source cell crosses over.
/// </para>
/// <para>
/// Every unwrapped copy of key material is zeroed after use, and no key material is logged.
/// </para>
/// </remarks>
// See ADR-1514.
[PublicAPI]
public interface IMasterKeyTransfer
{
    /// <summary>
    /// Seals the master keys of an owner to a recipient key.
    /// </summary>
    /// <param name="owner">The <see cref="SecretOwner"/> whose master keys to export.</param>
    /// <param name="recipient">The <see cref="SecretKey"/> of the recipient, which is the public key that the package
    /// is encrypted to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the compact JWE. An
    /// owner without master keys yields a package with none.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when a master key of the owner cannot be unwrapped.
    /// </exception>
    ValueTask<string> ExportAsync(
        SecretOwner owner,
        SecretKey recipient,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Stores the master keys of a package for an owner: the root key the owner brings first, then each master key,
    /// wrapped under the nearest root key of the owner, with its identifier and version. The root key the owner brings
    /// passes the checks that <see cref="IOwnerRootKeyManager.SetAsync"/> applies, a registered policy that lets the
    /// owner bring one and an allowed key wrapper with a key reference and an access identity that fit it, the cell's
    /// cloud regions included; a refused root key stores nothing.
    /// </summary>
    /// <param name="owner">The <see cref="SecretOwner"/> that the master keys belong to, which must be the owner that
    /// the package was exported for.</param>
    /// <param name="package">The compact JWE that <see cref="ExportAsync"/> produced.</param>
    /// <param name="recipientPrivate">The <see cref="SecretKey"/> of the recipient, which is the private key that
    /// decrypts the package.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the number of master
    /// keys that were stored, or the reason that the root key the package brings is refused.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when the package does not decrypt with the recipient key.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the decrypted package is malformed or names another owner.
    /// </exception>
    ValueTask<MasterKeyTransferResult> ImportAsync(
        SecretOwner owner,
        string package,
        SecretKey recipientPrivate,
        CancellationToken cancellationToken
    );
}
