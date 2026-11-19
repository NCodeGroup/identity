// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence;

/// <summary>
/// Provides an <see cref="ISurrogateKeyGenerator"/> for tests that mints UUIDv7 keys whose timestamp advances one
/// millisecond per key from a fixed instant, so the keys ascend in the order they are minted.
/// </summary>
public sealed class SequentialSurrogateKeyGenerator : ISurrogateKeyGenerator
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private long _count;

    /// <inheritdoc />
    public Guid CreateKey()
    {
        var count = Interlocked.Increment(ref _count);
        var timestamp = Start.AddMilliseconds(count);
        return Guid.CreateVersion7(timestamp);
    }
}
