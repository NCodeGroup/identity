// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the known values of <see cref="CellOptions.Kind"/>. The vocabulary is open: a deployment may name any other
/// kind, and code compares kinds ordinally without assuming these are all of them.
/// </summary>
[PublicAPI]
public static class CellKinds
{
    /// <summary>
    /// A cell that hosts the tenants of many teams.
    /// </summary>
    public const string Shared = "shared";

    /// <summary>
    /// A cell that is dedicated to the tenants of one team.
    /// </summary>
    public const string Dedicated = "dedicated";
}
