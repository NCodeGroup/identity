// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.SharedState.Valkey.Options;

namespace NCode.Identity.OpenId.Host;

/// <summary>
/// Appends the name of the cell and <c>:</c> to the configured <see cref="ValkeyOptions.KeyPrefix"/>, so every Valkey
/// key and channel starts <c>{KeyPrefix}{cell}:</c> and cells that share one Valkey server never read or write one
/// another's state. The default prefix gives <c>ncode-identity:{cell}:</c>. The prefix holds no braces: a clustered
/// server takes the hash tag of a key from its first <c>{</c> and the next <c>}</c>.
/// </summary>
/// <param name="cellOptionsAccessor">The options that name the cell.</param>
internal sealed class CellValkeyOptionsSetup(IOptions<CellOptions> cellOptionsAccessor)
    : IPostConfigureOptions<ValkeyOptions>
{
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, ValkeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.KeyPrefix = $"{options.KeyPrefix}{CellOptionsAccessor.Value.Name}:";
    }
}
