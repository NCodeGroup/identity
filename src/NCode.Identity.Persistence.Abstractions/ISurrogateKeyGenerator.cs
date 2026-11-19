// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// Mints the internal surrogate key of a stored row, a <see cref="Guid"/> holding a UUIDv7 (RFC 9562) that any process
/// mints on its own, without coordination or a database round trip, and that never reaches a caller.
/// </summary>
[PublicAPI]
public interface ISurrogateKeyGenerator
{
    /// <summary>
    /// Mints a new surrogate key.
    /// </summary>
    /// <returns>A UUIDv7 whose leading 48 bits are the Unix time in milliseconds when it was minted.</returns>
    Guid CreateKey();
}
