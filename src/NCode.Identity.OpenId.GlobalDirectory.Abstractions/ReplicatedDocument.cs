// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one document that the control-plane cell publishes to the directory and every other cell applies from the
/// change feed: the envelope of the platform's authority, a team's authority and the management keys.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ReplicatedDocument
{
    /// <summary>
    /// Gets the kind of the document, one of the <see cref="ReplicatedDocumentKinds"/> values.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the key of the document within its kind: the team identifier of a team authority document, and empty for
    /// every other kind.
    /// </summary>
    public string Key { get; init; } = "";

    /// <summary>
    /// Gets the revision of the document. A document replaces the stored one only when its revision is higher.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the content of the document, a JSON value whose shape the kind decides, or <c>null</c> for a retired
    /// document.
    /// </summary>
    public JsonElement? Content { get; init; }
}
