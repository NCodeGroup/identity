// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Json;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the platform's settings as the control-plane cell publishes them and every other cell mirrors them.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record PlatformDocument
{
    /// <summary>
    /// Gets the platform's settings, a JSON object.
    /// </summary>
    public JsonElement Settings { get; init; } = JsonElements.EmptyObject;

    /// <summary>
    /// Gets the sequence number of the directory change that published this document, or <c>0</c> for a document that
    /// is not yet published.
    /// </summary>
    public long Sequence { get; init; }
}
