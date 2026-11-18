// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/// <summary>
/// Names the members of the tenant transfer package's payload and its format version. The payload is
/// <c>{ version, region, targetCell, tenantId, sections: [{ name, json }], masterKeys }</c>. A cell imports only a
/// package of its own <see cref="FormatVersion"/>, and a section's shape changes only with a new format version.
/// </summary>
// See ADR-1514.
internal static class TenantTransferPackage
{
    /// <summary>
    /// The format version of the package that the export writes and the import reads.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The member that holds the package's format version.
    /// </summary>
    public const string VersionMember = "version";

    /// <summary>
    /// The member that holds the region of the cell that exports the package.
    /// </summary>
    public const string RegionMember = "region";

    /// <summary>
    /// The member that holds the name of the cell that the package is offered to, the only cell that imports it.
    /// </summary>
    public const string TargetCellMember = "targetCell";

    /// <summary>
    /// The member that holds the identifier of the tenant.
    /// </summary>
    public const string TenantIdMember = "tenantId";

    /// <summary>
    /// The member that holds the array of sections.
    /// </summary>
    public const string SectionsMember = "sections";

    /// <summary>
    /// The member of a section that holds its name.
    /// </summary>
    public const string SectionNameMember = "name";

    /// <summary>
    /// The member of a section that holds its JSON value.
    /// </summary>
    public const string SectionJsonMember = "json";

    /// <summary>
    /// The member that holds the tenant's master keys sealed to the recipient key.
    /// </summary>
    public const string MasterKeysMember = "masterKeys";
}
