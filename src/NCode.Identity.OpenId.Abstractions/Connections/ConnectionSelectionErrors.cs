// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the machine-readable codes of why <see cref="ConnectionSelection"/> refuses a request.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionSelectionErrors
{
    /// <summary>
    /// Several connections are offered and the request neither named one nor preselects one.
    /// </summary>
    public const string Required = "required";

    /// <summary>
    /// The named connection is not offered to the client, or no connection is offered at all.
    /// </summary>
    public const string Unavailable = "unavailable";
}
