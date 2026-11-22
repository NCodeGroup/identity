// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents the response to creating an audit stream: the stream and its first signing secret, whose value is shown
/// only here.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CreatedAuditStreamResource
{
    /// <summary>
    /// Gets the created audit stream.
    /// </summary>
    public required AuditStreamResource AuditStream { get; init; }

    /// <summary>
    /// Gets the first signing secret of the audit stream, including its value.
    /// </summary>
    public required CreatedAuditStreamSigningSecretResource SigningSecret { get; init; }
}
