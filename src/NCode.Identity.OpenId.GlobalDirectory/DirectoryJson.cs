// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Holds the JSON options of the wire form of the directory records.
/// </summary>
internal static class DirectoryJson
{
    /// <summary>
    /// The JSON options of the directory API: web defaults, so property names are camel case.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
