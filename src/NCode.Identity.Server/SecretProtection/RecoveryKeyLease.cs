// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;

namespace NCode.Identity.Server.SecretProtection;

/// <summary>
/// Holds the sensitive PEM text of the recovery private key until the command ends, and zeroes it when it is disposed.
/// </summary>
// See ADR-1408.
internal sealed class RecoveryKeyLease(Secret<char> pem) : IDisposable
{
    /// <summary>
    /// Gets the PEM text of the recovery private key.
    /// </summary>
    public ReadOnlyMemory<char> Pem => pem.RevealMemory();

    /// <inheritdoc />
    public void Dispose() => pem.Dispose();
}
