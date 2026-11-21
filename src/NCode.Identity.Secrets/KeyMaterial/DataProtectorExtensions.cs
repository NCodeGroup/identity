// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.DataProtection;
using NCode.Buffers;
using NCode.Extensions.DataProtection;

namespace NCode.Identity.Secrets.KeyMaterial;

/// <summary>
/// Provides extension methods for <see cref="IDataProtector"/>.
/// </summary>
[PublicAPI]
public static class DataProtectorExtensions
{
    /// <param name="dataProtector">The <see cref="IDataProtector"/> instance.</param>
    extension(IDataProtector dataProtector)
    {
        /// <summary>
        /// Cryptographically unprotects asymmetric key material that is <c>PKCS#8</c> encoded.
        /// </summary>
        /// <param name="protectedPkcs8PrivateKey">The protected asymmetric key material that is <c>PKCS#8</c> encoded.</param>
        /// <param name="algorithmFactory">Factory method that can be used to create <typeparamref name="T"/> instances.</param>
        /// <typeparam name="T">The type of the <see cref="AsymmetricAlgorithm"/>.</typeparam>
        /// <returns>The newly created asymmetric key initialized with it's corresponding key material.</returns>
        public T ExportAsymmetricAlgorithm<T>(
            ReadOnlySpan<byte> protectedPkcs8PrivateKey,
            Func<T> algorithmFactory
        )
            where T : AsymmetricAlgorithm
        {
            using var privateKeyBuffer = BufferFactory.CreateContiguousBufferWriter(
                isSensitive: true
            );
            IBufferWriter<byte> privateKeyWriter = privateKeyBuffer;

            dataProtector.UnprotectSpan(protectedPkcs8PrivateKey, ref privateKeyWriter);

            var algorithm = algorithmFactory();
            try
            {
                algorithm.ImportPkcs8PrivateKey(privateKeyBuffer.WrittenSpan, out var bytesRead);
                Debug.Assert(bytesRead == privateKeyBuffer.WrittenCount);
            }
            catch
            {
                algorithm.Dispose();
                throw;
            }

            return algorithm;
        }
    }
}
