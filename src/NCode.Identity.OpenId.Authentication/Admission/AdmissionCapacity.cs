// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Provides the bounds of the share of a replica's request capacity a tenant may fill, as a percentage.
/// </summary>
// See ADR-1523.
internal static class AdmissionCapacity
{
    /// <summary>
    /// The smallest share a tenant may be given.
    /// </summary>
    internal const int MinPercent = 1;

    /// <summary>
    /// The whole capacity, which a tenant without the setting may fill.
    /// </summary>
    internal const int FullPercent = 100;
}
