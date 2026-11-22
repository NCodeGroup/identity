// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents the mutable metadata of an audit stream that a JSON Patch document may modify.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class UpdateAuditStreamRequest
{
    /// <summary>
    /// Gets or sets the human-readable name for the audit stream.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the HTTPS endpoint that receives the audit records.
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the audit stream delivers records.
    /// </summary>
    public required bool IsEnabled { get; set; }
}
