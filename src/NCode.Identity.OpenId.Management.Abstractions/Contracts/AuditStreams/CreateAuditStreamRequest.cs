// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents the request to create an audit stream.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CreateAuditStreamRequest
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
    /// Gets or sets a value indicating whether the audit stream delivers records. The default is <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
