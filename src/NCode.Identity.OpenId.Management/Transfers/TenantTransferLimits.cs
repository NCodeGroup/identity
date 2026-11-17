// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Holds the ceiling of a tenant move. The whole tenant, its local accounts included, travels as one compact <c>JWE</c>
/// in one request, so the export refuses a package over <see cref="MaxPackageBytes"/> and the import route admits a
/// request body of at most <see cref="MaxImportRequestBytes"/>.
/// </summary>
// See ADR-1514.
internal static class TenantTransferLimits
{
    /// <summary>
    /// The largest package, in bytes, that a tenant move carries: 64 MiB.
    /// </summary>
    public const int MaxPackageBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The largest request body, in bytes, that the import route admits: the largest package and 64 KiB for the
    /// transfer key's <c>kid</c> and the JSON around them.
    /// </summary>
    public const long MaxImportRequestBytes = MaxPackageBytes + (64 * 1024);
}
