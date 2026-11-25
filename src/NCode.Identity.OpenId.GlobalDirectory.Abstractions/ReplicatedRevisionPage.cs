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
    /// <summary>
    /// Gets the documents of the page, each with a <c>null</c> <see cref="ReplicatedDocument.Content"/>.
    /// </summary>
    public required IReadOnlyList<ReplicatedDocument> Revisions { get; init; }

    /// <summary>
    /// Gets the continuation that lists the page after this one, or <c>null</c> when this page is the last.
    /// </summary>
    public string? Continuation { get; init; }
}
