// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the outcome of selecting the connection a request signs in through.
/// </summary>
// See ADR-1535.
[PublicAPI]
public sealed class ConnectionSelection
{
    /// <summary>
    /// Gets the selected connection, or <see langword="null"/> when the request is refused or no connection is
    /// preselected.
    /// </summary>
    public ResolvedConnection? Connection { get; init; }

    /// <summary>
    /// Gets the machine-readable code of why the request is refused, one of the <see cref="ConnectionSelectionErrors"/>
    /// values, or <see langword="null"/> when it is not refused.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the description of why the request is refused, or <see langword="null"/> when it is not refused.
    /// </summary>
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// Gets a value indicating whether the request is refused.
    /// </summary>
    public bool IsRefused => ErrorDescription is not null;
}
