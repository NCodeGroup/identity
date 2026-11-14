// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the platform's settings as the control-plane cell publishes them and every other cell mirrors them.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record PlatformDocument
{
    /// <summary>
    /// Gets the platform's settings as a JSON object.
    /// </summary>
    public string SettingsJson { get; init; } = "{}";

    /// <summary>
    /// Gets the sequence number of the directory change that published this document, or <c>0</c> for a document that
    /// is not yet published.
    /// </summary>
    public long Sequence { get; init; }
}
