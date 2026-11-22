// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;
using NCode.Disposables;

namespace NCode.Identity.Jose.Algorithms.Compression;

internal static class CompressionAlgorithmExtensions
{
    extension(CompressionAlgorithm? algorithm)
    {
        public IDisposable Compress(
            IDictionary<string, object> header,
            ReadOnlySpan<byte> uncompressedData,
            out ReadOnlySpan<byte> compressedData
        )
        {
            if (algorithm == null || algorithm.Code == AlgorithmCodes.Compression.None)
            {
                compressedData = uncompressedData;
                return Disposable.Empty;
            }

            var initialCapacity = Math.Min(uncompressedData.Length, 1024);
            var buffer = BufferFactory.CreateContiguousBufferWriter(
                isSensitive: true,
                initialCapacity
            );

            try
            {
                algorithm.Compress(uncompressedData, buffer);
                header[JoseClaimNames.Header.Zip] = algorithm.Code;

                compressedData = buffer.WrittenSpan;
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }
    }
}
