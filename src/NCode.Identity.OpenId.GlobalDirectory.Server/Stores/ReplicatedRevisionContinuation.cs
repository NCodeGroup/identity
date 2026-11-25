// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Builds and reads the continuation of a page of replicated document revisions, which is the kind and the key of the
/// last document of the page. A caller of the directory treats the continuation as an opaque token.
/// </summary>
internal static class ReplicatedRevisionContinuation
{
    private const char Separator = ':';

    /// <summary>
    /// Builds the continuation that follows the document with a kind and a key.
    /// </summary>
    /// <param name="kind">The kind of the last document of a page, which never contains the separator.</param>
    /// <param name="key">The key of the last document of a page.</param>
    /// <returns>The continuation.</returns>
    public static string Create(string kind, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(key);

        if (kind.Contains(Separator))
            throw new ArgumentException("The kind must not contain the separator.", nameof(kind));

        return $"{kind}{Separator}{key}";
    }

    /// <summary>
    /// Reads a continuation that <see cref="Create"/> built.
    /// </summary>
    /// <param name="continuation">The continuation to read.</param>
    /// <param name="kind">The kind after which the listing starts, when the continuation is valid.</param>
    /// <param name="key">The key after which the listing starts, when the continuation is valid.</param>
    /// <returns><c>true</c> when the continuation is valid; otherwise, <c>false</c>.</returns>
    public static bool TryParse(
        string? continuation,
        [NotNullWhen(true)] out string? kind,
        [NotNullWhen(true)] out string? key
    )
    {
        kind = null;
        key = null;

        var index = continuation?.IndexOf(Separator) ?? -1;
        if (continuation is null || index <= 0)
            return false;

        kind = continuation[..index];
        key = continuation[(index + 1)..];
        return true;
    }
}
