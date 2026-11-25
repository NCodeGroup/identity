// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one page of the revisions that the directory holds for its replicated documents, ordered by kind and then
/// by key.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ReplicatedRevisionPage
{
    private const char Separator = ':';

    /// <summary>
    /// Gets the documents of the page, each with a <c>null</c> <see cref="ReplicatedDocument.Content"/>.
    /// </summary>
    public required IReadOnlyList<ReplicatedDocument> Revisions { get; init; }

    /// <summary>
    /// Gets the continuation that lists the page after this one, or <c>null</c> when this page is the last.
    /// </summary>
    public string? Continuation { get; init; }

    /// <summary>
    /// Builds the continuation that follows the document with a kind and a key.
    /// </summary>
    /// <param name="kind">The kind of the last document of a page.</param>
    /// <param name="key">The key of the last document of a page.</param>
    /// <returns>The continuation.</returns>
    public static string CreateContinuation(string kind, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(key);

        return $"{kind}{Separator}{key}";
    }

    /// <summary>
    /// Reads a continuation that <see cref="CreateContinuation"/> built.
    /// </summary>
    /// <param name="continuation">The continuation to read.</param>
    /// <param name="kind">The kind after which the listing starts, when the continuation is valid.</param>
    /// <param name="key">The key after which the listing starts, when the continuation is valid.</param>
    /// <returns><c>true</c> when the continuation is valid; otherwise, <c>false</c>.</returns>
    public static bool TryParseContinuation(
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
