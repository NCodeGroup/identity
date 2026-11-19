// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Persistence;

/// <summary>
/// Provides the default implementation of <see cref="ISurrogateKeyGenerator"/>, which mints a UUIDv7 from the wall
/// clock and random bits.
/// </summary>
// See ADR-1517.
internal sealed class DefaultSurrogateKeyGenerator : ISurrogateKeyGenerator
{
    /// <inheritdoc />
    public Guid CreateKey() => Guid.CreateVersion7();
}
