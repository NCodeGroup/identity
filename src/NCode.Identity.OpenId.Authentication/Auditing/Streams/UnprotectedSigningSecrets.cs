// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Holds the unprotected signing secrets of one audit stream's delivery, each in a sensitive rental that is zeroed and
/// returned when the instance is disposed, so no secret lives in an ordinary array across the delivery's awaits.
/// </summary>
// See ADR-1534 and ADR-1266.
internal sealed class UnprotectedSigningSecrets : IDisposable
{
    private List<IDisposable> Leases { get; } = [];

    private List<Memory<byte>> Secrets { get; } = [];

    private bool IsDisposed { get; set; }

    /// <summary>
    /// Gets the number of secrets held.
    /// </summary>
    public int Count => Secrets.Count;

    /// <summary>
    /// Gets the secret at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the secret.</param>
    /// <exception cref="ObjectDisposedException">The secrets have been disposed.</exception>
    public ReadOnlySpan<byte> this[int index]
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return Secrets[index].Span;
        }
    }

    /// <summary>
    /// Copies a secret into a sensitive rental that this instance owns.
    /// </summary>
    /// <param name="secret">The unprotected secret to hold.</param>
    /// <exception cref="ObjectDisposedException">The secrets have been disposed.</exception>
    public void Add(ReadOnlySpan<byte> secret)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var lease = BufferFactory.Rent(secret.Length, isSensitive: true, out Memory<byte> memory);
        try
        {
            secret.CopyTo(memory.Span);
            Leases.Add(lease);
            Secrets.Add(memory);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        foreach (var lease in Leases)
        {
            lease.Dispose();
        }

        Leases.Clear();
        Secrets.Clear();
    }
}
